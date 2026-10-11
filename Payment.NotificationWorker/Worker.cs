using Microsoft.Extensions.Options;
using Payment.Contracts.Events.Payments;
using Payment.Messaging.Abstractions.Consuming;
using Payment.NotificationWorker.Messaging.RabbitMq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace Payment.NotificationWorker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly RabbitMqOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;

        private readonly string _workerId =
            $"{Environment.MachineName}-{Guid.NewGuid():N}";

        private IConnection? _connection;
        private IChannel? _channel;
        private readonly SemaphoreSlim _deliveryGate =
    new(1, 1);

        private string? _consumerTag;
        public Worker(
            ILogger<Worker> logger,
            IOptions<RabbitMqOptions> options,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _options = options.Value;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                ClientProvidedName = "Payment.NotificationWorker",

                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true
            };

            _connection =
                await factory.CreateConnectionAsync(
                    stoppingToken);

            _channel =
                await _connection.CreateChannelAsync(
                    cancellationToken: stoppingToken);

            await _channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: 1,
                global: false,
                cancellationToken: stoppingToken);

            var consumer =
                new AsyncEventingBasicConsumer(
                    _channel);

            consumer.ReceivedAsync += async (sender, args) =>
            {
                var gateAcquired = false;

                PaymentCompletedEvent? paymentEvent = null;

                try
                {
                    await _deliveryGate.WaitAsync(
                        stoppingToken);

                    gateAcquired = true;
                    // =====================================================
                    // DESERIALIZE
                    // =====================================================

                    var body =
                        args.Body.ToArray();

                    var json =
                        Encoding.UTF8.GetString(body);

                    paymentEvent =
                        JsonSerializer.Deserialize<PaymentCompletedEvent>(
                            json,
                            new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });

                    if (paymentEvent is null)
                    {
                        throw new InvalidOperationException(
                            "Payment event could not be deserialized.");
                    }

                    using var logScope =
                        _logger.BeginScope(
                            new Dictionary<string, object>
                            {
                                ["CorrelationId"] =
                                    paymentEvent.CorrelationId,

                                ["EventId"] =
                                    paymentEvent.EventId,

                                ["PaymentId"] =
                                    paymentEvent.PaymentId,

                                ["OrderId"] =
                                    paymentEvent.OrderId,

                                ["WorkerId"] =
                                    _workerId
                            });

                    // =====================================================
                    // CREATE DI SCOPE
                    // Her RabbitMQ mesajý kendi scope'una sahip.
                    // DbContext ve Scoped servisler burada oluþturulur.
                    // =====================================================

                    using var scope =
                        _scopeFactory.CreateScope();

                    var processedMessageStore =
                        scope.ServiceProvider
                            .GetRequiredService<
                                IProcessedMessageStore>();

                    var handler =
                        scope.ServiceProvider
                            .GetRequiredService<
                                IIntegrationEventHandler<
                                    PaymentCompletedEvent>>();

                    // =====================================================
                    // ATOMIC INBOX RESERVATION
                    // =====================================================

                    var leaseDuration = TimeSpan.FromMinutes(2);

                    var heartbeatInterval =  TimeSpan.FromSeconds(30);
                    var reservationResult =
                        await processedMessageStore.TryReserveAsync(
                            eventId: paymentEvent.EventId,
                            lockId: _workerId,
                            nowUtc: DateTime.UtcNow,
                            lockDuration: leaseDuration,
                            cancellationToken: stoppingToken);

                    // =====================================================
                    // ALREADY PROCESSED
                    // Event daha önce baþarýyla iþlendi.
                    // Handler tekrar çalýþtýrýlmaz.
                    // =====================================================

                    if (reservationResult ==
                        ProcessedMessageReservationResult.AlreadyProcessed)
                    {
                        _logger.LogWarning(
                            "Duplicate event ignored. EventId={EventId}, CorrelationId={CorrelationId}",
                            paymentEvent.EventId,
                            paymentEvent.CorrelationId);

                        await _channel.BasicAckAsync(
                            deliveryTag:
                                args.DeliveryTag,
                            multiple:
                                false,
                            cancellationToken:
                                stoppingToken);

                        return;
                    }

                    // =====================================================
                    // IN PROGRESS
                    // Baþka worker þu anda bu event'i iþliyor.
                    // Handler çalýþtýrýlmaz.
                    // Retry queue'ya tekrar gönderilmez.
                    // =====================================================

                    if (reservationResult ==
     ProcessedMessageReservationResult.InProgress)
                    {
                        var currentRetryCount =
                            GetRetryCount(
                                args.BasicProperties);

                        _logger.LogWarning(
                            "Event is already being processed by another worker. " +
                            "Message will be deferred through the retry queue. " +
                            "EventId={EventId}, WorkerId={WorkerId}",
                            paymentEvent.EventId,
                            _workerId);

                        await PublishToRetryAsync(
                            args.Body,
                            currentRetryCount,
                            stoppingToken);

                        await _channel.BasicAckAsync(
                            deliveryTag:
                                args.DeliveryTag,
                            multiple:
                                false,
                            cancellationToken:
                                stoppingToken);

                        return;
                    }

                    // =====================================================
                    // RESERVATION ACQUIRED
                    // Bu noktadan itibaren handler'ý yalnýzca reservation
                    // sahibi çalýþtýrabilir.
                    // =====================================================

                    _logger.LogInformation(
                        "Inbox reservation acquired. EventId={EventId}, WorkerId={WorkerId}",
                        paymentEvent.EventId,
                        _workerId);
                    using var leaseCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        stoppingToken);

                    using var processingCancellation =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            stoppingToken);

                    var leaseTask =
                        RenewLeaseLoopAsync(
                            eventId: paymentEvent.EventId,
                            lockId: _workerId,
                            leaseDuration: leaseDuration,
                            heartbeatInterval: heartbeatInterval,
                            processingCancellation: processingCancellation,
                            cancellationToken: leaseCancellation.Token);

                    try
                    {
                        _logger.LogInformation(
    "Processing integration event.");

                        await handler.HandleAsync(
                            paymentEvent,
                            processingCancellation.Token);

                        _logger.LogInformation(
                            "Integration event handler completed successfully.");

                        // Handler bitti.
                        // MarkAsProcessed çalýþýrken heartbeat paralel çalýþmasýn.
                        leaseCancellation.Cancel();

                        try
                        {
                            await leaseTask;
                        }
                        catch (OperationCanceledException)
                        {
                            // Heartbeat'i bilinçli olarak durdurduk.
                        }

                        await processedMessageStore
                            .MarkAsProcessedAsync(
                                paymentEvent.EventId,
                                _workerId,
                                stoppingToken);

                        _logger.LogInformation(
                            "Event processed successfully. EventId={EventId}, CorrelationId={CorrelationId}, WorkerId={WorkerId}",
                            paymentEvent.EventId,
                            paymentEvent.CorrelationId,
                            _workerId);
                    }
                    catch (OperationCanceledException)
    when (stoppingToken.IsCancellationRequested)
                    {
                        leaseCancellation.Cancel();

                        try
                        {
                            await leaseTask;
                        }
                        catch (OperationCanceledException)
                        {
                            // Shutdown sýrasýnda heartbeat kontrollü durdu.
                        }
                        catch (Exception leaseException)
                        {
                            _logger.LogWarning(
                                leaseException,
                                "Error while stopping inbox lease heartbeat during shutdown. EventId={EventId}, WorkerId={WorkerId}",
                                paymentEvent.EventId,
                                _workerId);
                        }

                        _logger.LogInformation(
                            "Integration event processing cancelled because application shutdown was requested. EventId={EventId}, WorkerId={WorkerId}",
                            paymentEvent.EventId,
                            _workerId);

                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Hata durumunda heartbeat'i durdur.
                        leaseCancellation.Cancel();

                        try
                        {
                            await leaseTask;
                        }
                        catch (OperationCanceledException)
                        {
                            // Heartbeat'i bilinçli olarak durdurduk.
                        }
                        catch (Exception leaseException)
                        {
                            _logger.LogError(
                                leaseException,
                                "Error while stopping inbox lease heartbeat. EventId={EventId}, WorkerId={WorkerId}",
                                paymentEvent.EventId,
                                _workerId);
                        }

                        await processedMessageStore
                            .ReleaseAsync(
                                paymentEvent.EventId,
                                _workerId,
                                ex.Message,
                                stoppingToken);

                        _logger.LogWarning(
                            "Inbox reservation released after processing failure. EventId={EventId}, WorkerId={WorkerId}",
                            paymentEvent.EventId,
                            _workerId);

                        throw;
                    }

                    // =====================================================
                    // ACK
                    // Hem handler hem Inbox completion baþarýlý.
                    // =====================================================

                    await _channel.BasicAckAsync(
                        deliveryTag:
                            args.DeliveryTag,
                        multiple:
                            false,
                        cancellationToken:
                            stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    // Uygulama kapanýyorsa retry/DLQ üretmiyoruz.
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error while processing RabbitMQ message.");

                    if (_channel is null)
                    {
                        return;
                    }

                    var currentRetryCount =
                        GetRetryCount(
                            args.BasicProperties);

                    // =====================================================
                    // RETRY
                    // =====================================================

                    if (currentRetryCount <
                        _options.MaxRetryCount)
                    {
                        var nextRetryCount =
                            currentRetryCount + 1;

                        _logger.LogWarning(
                            "Message will be retried. Retry {RetryCount}/{MaxRetryCount}",
                            nextRetryCount,
                            _options.MaxRetryCount);

                        await PublishToRetryAsync(
                            args.Body,
                            nextRetryCount,
                            stoppingToken);
                    }
                    else
                    {
                        // =================================================
                        // DEAD LETTER
                        // =================================================

                        _logger.LogError(
                            "Maximum retry count reached. Message is moving to DLQ.");

                        await PublishToDeadLetterAsync(
                            args.Body,
                            currentRetryCount,
                            stoppingToken);
                    }

                    // Retry veya DLQ publish baþarýlý olduktan sonra
                    // mevcut delivery ACK edilir.
                    await _channel.BasicAckAsync(
                        deliveryTag:
                            args.DeliveryTag,
                        multiple:
                            false,
                        cancellationToken:
                            stoppingToken);
                }
                finally
                {
                    if (gateAcquired)
                    {
                        _deliveryGate.Release();
                    }
                }
            };

            _consumerTag =
                await _channel.BasicConsumeAsync(
                    queue:
                        _options.QueueName,
                    autoAck:
                        false,
                    consumer:
                        consumer,
                    cancellationToken:
                        stoppingToken);

            _logger.LogInformation(
                "Worker listening queue: {QueueName}. WorkerId={WorkerId}",
                _options.QueueName,
                _workerId);

            await Task.Delay(
                Timeout.Infinite,
                stoppingToken);
        }

        public override async Task StopAsync(
    CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Notification worker is stopping. WorkerId={WorkerId}",
                _workerId);

            // Yeni RabbitMQ mesajý gelmesini durdur.
            if (_channel is not null &&
                _channel.IsOpen &&
                !string.IsNullOrWhiteSpace(_consumerTag))
            {
                try
                {
                    await _channel.BasicCancelAsync(
                        _consumerTag,
                        noWait: false,
                        cancellationToken: cancellationToken);

                    _logger.LogInformation(
                        "RabbitMQ consumer cancelled. No new messages will be delivered. WorkerId={WorkerId}",
                        _workerId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "RabbitMQ consumer could not be cancelled cleanly. WorkerId={WorkerId}",
                        _workerId);
                }
            }

            // BackgroundService stoppingToken burada cancellation alýr.
            await base.StopAsync(
                cancellationToken);

            // Aktif bir delivery varsa callback'in bitmesini bekle.
            try
            {
                await _deliveryGate.WaitAsync(
                    cancellationToken);

                _deliveryGate.Release();
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Timed out while waiting for active RabbitMQ delivery to finish. WorkerId={WorkerId}",
                    _workerId);
            }

            // Channel kapat.
            if (_channel is not null)
            {
                await _channel.DisposeAsync();
                _channel = null;
            }

            // Connection kapat.
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            _logger.LogInformation(
                "Notification worker stopped. WorkerId={WorkerId}",
                _workerId);
        }


        private async Task RenewLeaseLoopAsync(
             Guid eventId,
             string lockId,
             TimeSpan leaseDuration,
             TimeSpan heartbeatInterval,
             CancellationTokenSource processingCancellation,
             CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(
                    heartbeatInterval,
                    cancellationToken);

                using var scope =
                    _scopeFactory.CreateScope();

                var processedMessageStore =
                    scope.ServiceProvider
                        .GetRequiredService<IProcessedMessageStore>();

                var renewed =
                    await processedMessageStore.RenewLeaseAsync(
                        eventId: eventId,
                        lockId: lockId,
                        nowUtc: DateTime.UtcNow,
                        lockDuration: leaseDuration,
                        cancellationToken: cancellationToken);

                if (!renewed)
                {
                    _logger.LogError(
                        "Inbox lease lost. Processing will be cancelled. EventId={EventId}, WorkerId={WorkerId}",
                        eventId,
                        lockId);

                    processingCancellation.Cancel();

                    return;
                }

                _logger.LogDebug(
                    "Inbox lease renewed. EventId={EventId}, WorkerId={WorkerId}",
                    eventId,
                    lockId);
            }
        }
        // =========================================================
        // RETRY COUNT
        // =========================================================

        private static int GetRetryCount(
            IReadOnlyBasicProperties properties)
        {
            if (properties.Headers is null)
            {
                return 0;
            }

            if (!properties.Headers.TryGetValue(
                "x-retry-count",
                out var value))
            {
                return 0;
            }

            return value switch
            {
                int intValue =>
                    intValue,

                long longValue =>
                    (int)longValue,

                byte byteValue =>
                    byteValue,

                _ =>
                    0
            };
        }

        // =========================================================
        // RETRY PUBLISH
        // =========================================================

        private async Task PublishToRetryAsync(
            ReadOnlyMemory<byte> body,
            int retryCount,
            CancellationToken cancellationToken)
        {
            if (_channel is null)
            {
                throw new InvalidOperationException(
                    "RabbitMQ channel is not available.");
            }

            var properties =
                new BasicProperties
                {
                    Persistent = true,

                    ContentType =
                        "application/json",

                    Headers =
                        new Dictionary<string, object?>
                        {
                            ["x-retry-count"] =
                                retryCount
                        }
                };

            await _channel.BasicPublishAsync(
                exchange:
                    _options.RetryExchangeName,
                routingKey:
                    _options.RetryRoutingKey,
                mandatory:
                    false,
                basicProperties:
                    properties,
                body:
                    body,
                cancellationToken:
                    cancellationToken);
        }

        // =========================================================
        // DEAD LETTER PUBLISH
        // =========================================================

        private async Task PublishToDeadLetterAsync(
            ReadOnlyMemory<byte> body,
            int retryCount,
            CancellationToken cancellationToken)
        {
            if (_channel is null)
            {
                throw new InvalidOperationException(
                    "RabbitMQ channel is not available.");
            }

            var properties =
                new BasicProperties
                {
                    Persistent = true,

                    ContentType =
                        "application/json",

                    Headers =
                        new Dictionary<string, object?>
                        {
                            ["x-retry-count"] =
                                retryCount
                        }
                };

            await _channel.BasicPublishAsync(
                exchange:
                    _options.DeadLetterExchangeName,
                routingKey:
                    _options.DeadLetterRoutingKey,
                mandatory:
                    false,
                basicProperties:
                    properties,
                body:
                    body,
                cancellationToken:
                    cancellationToken);
        }
    }
}