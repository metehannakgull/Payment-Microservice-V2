using Payment.Application.Abstractions.Outbox;
using Payment.Contracts.Events.Payments;
using Payment.Messaging.Abstractions.Publishing;
using System.Text.Json;
using Payment.Application.Abstractions.Observability;

namespace Payment.Api.BackgroundServices
{
    public sealed class OutboxPublisherWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OutboxPublisherWorker> _logger;
        private readonly IPaymentMetrics _paymentMetrics;
        private readonly string _workerId =
            $"{Environment.MachineName}-{Guid.NewGuid():N}";

        public OutboxPublisherWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<OutboxPublisherWorker> logger,
            IPaymentMetrics paymentMetrics)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _paymentMetrics = paymentMetrics;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "Outbox publisher worker started. WorkerId={WorkerId}",
                _workerId);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOutboxAsync(
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        "Unexpected error while processing outbox. WorkerId={WorkerId}, Error={Error}",
                        _workerId,
                        ex.Message);
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(2),
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task ProcessOutboxAsync(
    CancellationToken cancellationToken)
        {
            using var scope =
                _scopeFactory.CreateScope();

            var outboxStore =
                scope.ServiceProvider
                    .GetRequiredService<IOutboxStore>();

            var eventPublisher =
                scope.ServiceProvider
                    .GetRequiredService<IIntegrationEventPublisher>();

            var leaseDuration =
                TimeSpan.FromSeconds(60);

            var heartbeatInterval =
                TimeSpan.FromSeconds(20);

            var messages =
                await outboxStore.ClaimPendingAsync(
                    lockId: _workerId,
                    nowUtc: DateTime.UtcNow,
                    lockDuration: leaseDuration,
                    batchSize: 20,
                    cancellationToken: cancellationToken);

            if (messages.Count == 0)
            {
                return;
            }

            foreach (var message in messages)
            {
                using var leaseCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);

                using var leaseLostCancellation =
    new CancellationTokenSource();

                using var publishTimeoutCancellation =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(30));

                using var publishCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        leaseLostCancellation.Token,
                        publishTimeoutCancellation.Token);
                var leaseTask =
                    RenewLeaseLoopAsync(
                        outboxStore: outboxStore,
                        messageId: message.Id,
                        lockId: _workerId,
                        leaseDuration: leaseDuration,
                        heartbeatInterval: heartbeatInterval,
                        leaseLostCancellation: leaseLostCancellation,
                        cancellationToken: leaseCancellation.Token);

                try
                {
                   
                    // =====================================================
                    // PUBLISH
                    // =====================================================

                    await PublishMessageAsync(
                        message,
                        eventPublisher,
                        publishCancellation.Token);

                    // Publish tamamlandı.
                    // Artık heartbeat'in paralel devam etmesine gerek yok.
                    leaseCancellation.Cancel();

                    try
                    {
                        await leaseTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Heartbeat bilinçli olarak durduruldu.
                    }

                    // =====================================================
                    // FINALIZE
                    // =====================================================

                    await outboxStore.MarkAsProcessedAsync(
                        message.Id,
                        _workerId,
                        cancellationToken);

                    _paymentMetrics.OutboxPublishSucceeded(
    message.EventType);

                    _logger.LogInformation(
                        "Outbox message published successfully. MessageId={MessageId}, EventType={EventType}, WorkerId={WorkerId}",
                        message.Id,
                        message.EventType,
                        _workerId);
                }
                catch (OperationCanceledException)
    when (cancellationToken.IsCancellationRequested)
                {
                    await StopLeaseHeartbeatAsync(
                        leaseCancellation,
                        leaseTask,
                        message.Id);

                    _logger.LogInformation(
                        "Outbox processing cancelled because application shutdown was requested. MessageId={MessageId}, WorkerId={WorkerId}",
                        message.Id,
                        _workerId);

                    throw;
                }

                catch (OperationCanceledException)
                    when (
                        publishTimeoutCancellation.IsCancellationRequested &&
                        !leaseLostCancellation.IsCancellationRequested &&
                        !cancellationToken.IsCancellationRequested)
                {
                    await StopLeaseHeartbeatAsync(
                        leaseCancellation,
                        leaseTask,
                        message.Id);

                    await outboxStore.MarkAsFailedAsync(
                        message.Id,
                        _workerId,
                        "RabbitMQ publish timed out after 30 seconds.",
                        cancellationToken);
                    _paymentMetrics.OutboxPublishTimedOut(
    message.EventType);
                    _logger.LogWarning(
                        "RabbitMQ publish timed out. MessageId={MessageId}, EventType={EventType}, WorkerId={WorkerId}, TimeoutSeconds={TimeoutSeconds}",
                        message.Id,
                        message.EventType,
                        _workerId,
                        30);
                }
                
                catch (OperationCanceledException)
    when (
        leaseLostCancellation.IsCancellationRequested &&
        !cancellationToken.IsCancellationRequested)
                {
                    await StopLeaseHeartbeatAsync(
                        leaseCancellation,
                        leaseTask,
                        message.Id);
                    _paymentMetrics.OutboxOwnershipLost(
    message.EventType);
                    _logger.LogWarning(
                        "Outbox publish cancelled because lease ownership was lost. MessageId={MessageId}, WorkerId={WorkerId}",
                        message.Id,
                        _workerId);

                    // Artık mesajın sahibi olmadığımız için
                    // DB state'ine dokunmuyoruz.
                }
                // =====================================================
                // OWNERSHIP LOST
                // =====================================================

                catch (OutboxOwnershipLostException ex)
                {
                    await StopLeaseHeartbeatAsync(
                        leaseCancellation,
                        leaseTask,
                        message.Id);
                    _paymentMetrics.OutboxOwnershipLost(
    message.EventType);
                    _logger.LogWarning(
                        "Outbox ownership lost. Message state will not be modified by this worker. " +
                        "MessageId={MessageId}, WorkerId={WorkerId}, Error={Error}",
                        message.Id,
                        _workerId,
                        ex.Message);
                }

                // =====================================================
                // TRANSIENT FAILURE
                // =====================================================

                catch (IntegrationEventPublishException ex)
                    when (ex.FailureKind == PublishFailureKind.Transient)
                {
                    await StopLeaseHeartbeatAsync(
                        leaseCancellation,
                        leaseTask,
                        message.Id);

                    await outboxStore.MarkAsFailedAsync(
                        message.Id,
                        _workerId,
                        ex.Message,
                        cancellationToken);
                    _paymentMetrics.OutboxPublishFailed(
    message.EventType,
    "transient");
                    _logger.LogWarning(
                        "Transient outbox publish failure. MessageId={MessageId}, EventType={EventType}, WorkerId={WorkerId}, RetryCount={RetryCount}, Error={Error}",
                        message.Id,
                        message.EventType,
                        _workerId,
                        message.RetryCount + 1,
                        ex.Message);
                }

                // =====================================================
                // PERMANENT FAILURE
                // =====================================================

                catch (IntegrationEventPublishException ex)
                    when (ex.FailureKind == PublishFailureKind.Permanent)
                {
                    await StopLeaseHeartbeatAsync(
                        leaseCancellation,
                        leaseTask,
                        message.Id);

                    await outboxStore.MarkAsDeadLetterAsync(
                        message.Id,
                        _workerId,
                        ex.Message,
                        cancellationToken);
                    _paymentMetrics.OutboxPublishFailed(
    message.EventType,
    "permanent");
                    _logger.LogError(
                        "Permanent outbox publish failure. Message dead-lettered. MessageId={MessageId}, EventType={EventType}, WorkerId={WorkerId}, Error={Error}",
                        message.Id,
                        message.EventType,
                        _workerId,
                        ex.Message);
                }

                // =====================================================
                // BAD PAYLOAD
                // =====================================================

                catch (JsonException ex)
                {
                    await StopLeaseHeartbeatAsync(
                        leaseCancellation,
                        leaseTask,
                        message.Id);

                    await outboxStore.MarkAsDeadLetterAsync(
                        message.Id,
                        _workerId,
                        ex.Message,
                        cancellationToken);
                    _paymentMetrics.OutboxPublishFailed(
    message.EventType,
    "bad_payload");

                    _logger.LogError(
                        ex,
                        "Outbox payload deserialization failed. MessageId={MessageId}, EventType={EventType}, WorkerId={WorkerId}",
                        message.Id,
                        message.EventType,
                        _workerId);
                }

                // =====================================================
                // UNSUPPORTED EVENT
                // =====================================================

                catch (NotSupportedException ex)
                {
                    await StopLeaseHeartbeatAsync(
                        leaseCancellation,
                        leaseTask,
                        message.Id);

                    await outboxStore.MarkAsDeadLetterAsync(
                        message.Id,
                        _workerId,
                        ex.Message,
                        cancellationToken);
                    _paymentMetrics.OutboxPublishFailed(
    message.EventType,
    "unsupported_event");

                    _logger.LogError(
                        ex,
                        "Unsupported outbox event. MessageId={MessageId}, EventType={EventType}, WorkerId={WorkerId}",
                        message.Id,
                        message.EventType,
                        _workerId);
                }

                // =====================================================
                // UNEXPECTED FAILURE
                // =====================================================

                catch (Exception ex)
                {
                    await StopLeaseHeartbeatAsync(
                        leaseCancellation,
                        leaseTask,
                        message.Id);

                    try
                    {
                        await outboxStore.MarkAsFailedAsync(
                            message.Id,
                            _workerId,
                            ex.Message,
                            cancellationToken);
                        _paymentMetrics.OutboxPublishFailed(
    message.EventType,
    "unexpected");
                    }
                    catch (OutboxOwnershipLostException ownershipException)
                    {
                        _paymentMetrics.OutboxOwnershipLost(
   message.EventType);
                        _logger.LogWarning(
                            "Outbox ownership was lost while persisting failure state. MessageId={MessageId}, WorkerId={WorkerId}, Error={Error}",
                            message.Id,
                            _workerId,
                            ownershipException.Message);
                       
                        continue;
                    }

                    _logger.LogError(
                        ex,
                        "Unexpected outbox publish failure. MessageId={MessageId}, EventType={EventType}, WorkerId={WorkerId}",
                        message.Id,
                        message.EventType,
                        _workerId);
                }
            }
        }

        private static async Task PublishMessageAsync(
            OutboxMessage message,
            IIntegrationEventPublisher eventPublisher,
            CancellationToken cancellationToken)
        {
            
            switch (message.EventType)
            {
                case nameof(PaymentCompletedEvent):
                    {
                        var integrationEvent =
                            JsonSerializer.Deserialize<PaymentCompletedEvent>(
                                message.Payload);

                        if (integrationEvent is null)
                        {
                            throw new InvalidOperationException(
                                "PaymentCompletedEvent could not be deserialized.");
                        }

                        await eventPublisher.PublishAsync(
                            integrationEvent,
                            cancellationToken);

                        break;
                    }

                case nameof(PaymentFailedEvent):
                    {
                        var integrationEvent =
                            JsonSerializer.Deserialize<PaymentFailedEvent>(
                                message.Payload);

                        if (integrationEvent is null)
                        {
                            throw new InvalidOperationException(
                                "PaymentFailedEvent could not be deserialized.");
                        }

                        await eventPublisher.PublishAsync(
                            integrationEvent,
                            cancellationToken);

                        break;
                    }

                default:
                    throw new NotSupportedException(
                        $"Unsupported outbox event type: {message.EventType}");
            }
        }

        private async Task RenewLeaseLoopAsync(
        IOutboxStore outboxStore,
        Guid messageId,
        string lockId,
        TimeSpan leaseDuration,
        TimeSpan heartbeatInterval,
        CancellationTokenSource leaseLostCancellation,
        CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(
                    heartbeatInterval,
                    cancellationToken);

                var newLockedUntilUtc =
                    DateTime.UtcNow.Add(
                        leaseDuration);

                var renewed =
                    await outboxStore.RenewLockAsync(
                        messageId,
                        lockId,
                        newLockedUntilUtc,
                        cancellationToken);

                if (!renewed)
                {
                    _logger.LogError(
                        "Outbox lease lost. Publish processing will be cancelled. MessageId={MessageId}, WorkerId={WorkerId}",
                        messageId,
                        lockId);

                    leaseLostCancellation.Cancel();

                    return;
                }
            }
        }
        private async Task StopLeaseHeartbeatAsync(
            CancellationTokenSource leaseCancellation,
            Task leaseTask,
            Guid messageId)
        {
            leaseCancellation.Cancel();

            try
            {
                await leaseTask;
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error while stopping Outbox lease heartbeat. MessageId={MessageId}, WorkerId={WorkerId}",
                    messageId,
                    _workerId);
            }
        }

        public override async Task StopAsync(
    CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Outbox publisher worker is stopping. WorkerId={WorkerId}",
                _workerId);

            try
            {
                await base.StopAsync(
                    cancellationToken);
            }
            finally
            {
                _logger.LogInformation(
                    "Outbox publisher worker stopped. WorkerId={WorkerId}",
                    _workerId);
            }
        }
    }
}