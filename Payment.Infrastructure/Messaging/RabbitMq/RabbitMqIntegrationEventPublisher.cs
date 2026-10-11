using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.Contracts.Events.Payments;
using Payment.Messaging.Abstractions.Publishing;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System.Text;
using System.Text.Json;

namespace Payment.Infrastructure.Messaging.RabbitMq
{
    public sealed class RabbitMqIntegrationEventPublisher
        : IIntegrationEventPublisher
    {
        private readonly RabbitMqConnection _connection;
        private readonly RabbitMqOptions _options;
        private readonly ILogger<RabbitMqIntegrationEventPublisher> _logger;

        public RabbitMqIntegrationEventPublisher(
            RabbitMqConnection connection,
            IOptions<RabbitMqOptions> options,
            ILogger<RabbitMqIntegrationEventPublisher> logger)
        {
            _connection = connection;
            _options = options.Value;
            _logger = logger;
        }

        public async Task PublishAsync<T>(
            T message,
            CancellationToken cancellationToken = default)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(message);

            var eventType =
                typeof(T).Name;

            var routingKey =
                GetRoutingKey(message);

            var eventId =
                GetEventId(message);

            var correlationId =
                GetCorrelationId(message);

            using var logScope =
                _logger.BeginScope(
                    new Dictionary<string, object?>
                    {
                        ["EventType"] = eventType,
                        ["EventId"] = eventId,
                        ["CorrelationId"] = correlationId,
                        ["RoutingKey"] = routingKey
                    });

            try
            {
                _logger.LogInformation(
                    "Publishing RabbitMQ integration event.");

                // =====================================================
                // CONNECTION
                // =====================================================

                var connection =
                    await _connection.GetConnectionAsync(
                        cancellationToken);

                if (!connection.IsOpen)
                {
                    throw new AlreadyClosedException(
                        new ShutdownEventArgs(
                            ShutdownInitiator.Application,
                            0,
                            "RabbitMQ connection is not open."));
                }

                // =====================================================
                // PUBLISHER CONFIRM CHANNEL
                // =====================================================

                var channelOptions =
                    new CreateChannelOptions(
                        publisherConfirmationsEnabled: true,
                        publisherConfirmationTrackingEnabled: true);

                await using var channel =
                    await connection.CreateChannelAsync(
                        channelOptions,
                        cancellationToken);

                // =====================================================
                // EXCHANGE
                // =====================================================

                await channel.ExchangeDeclareAsync(
                    exchange: _options.ExchangeName,
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: cancellationToken);

                // =====================================================
                // SERIALIZATION
                // =====================================================

                var json =
                    JsonSerializer.Serialize(message);

                var body =
                    Encoding.UTF8.GetBytes(json);

                // =====================================================
                // MESSAGE PROPERTIES
                // =====================================================

                var properties =
                    new BasicProperties
                    {
                        Persistent = true,
                        ContentType = "application/json",
                        ContentEncoding = "utf-8",
                        Type = eventType,
                        MessageId = eventId?.ToString(),
                        CorrelationId = correlationId
                    };

                // =====================================================
                // PUBLISH + CONFIRM
                // =====================================================

                await channel.BasicPublishAsync(
                    exchange: _options.ExchangeName,
                    routingKey: routingKey,
                    mandatory: true,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "RabbitMQ integration event confirmed by broker.");
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "RabbitMQ publish cancelled.");

                throw;
            }
            catch (BrokerUnreachableException ex)
            {
                throw new IntegrationEventPublishException(
                    "RabbitMQ broker is unreachable.",
                    PublishFailureKind.Transient,
                    ex);
            }
            catch (ConnectFailureException ex)
            {
                throw new IntegrationEventPublishException(
                    "RabbitMQ connection failed.",
                    PublishFailureKind.Transient,
                    ex);
            }
            catch (AlreadyClosedException ex)
            {
                throw new IntegrationEventPublishException(
                    "RabbitMQ connection or channel is closed.",
                    PublishFailureKind.Transient,
                    ex);
            }
            catch (PublishException ex)
            {
                throw new IntegrationEventPublishException(
                    "RabbitMQ rejected or returned the published message.",
                    PublishFailureKind.Permanent,
                    ex);
            }
            catch (IOException ex)
            {
                throw new IntegrationEventPublishException(
                    "RabbitMQ I/O failure occurred during publish.",
                    PublishFailureKind.Transient,
                    ex);
            }
            catch (Exception ex)
            {
                throw new IntegrationEventPublishException(
                    "Unexpected RabbitMQ publish failure.",
                    PublishFailureKind.Transient,
                    ex);
            }
        }

        private static string GetRoutingKey<T>(
            T message)
            where T : class
        {
            return message switch
            {
                PaymentCompletedEvent =>
                    "payment.completed",

                PaymentFailedEvent =>
                    "payment.failed",

                _ =>
                    throw new NotSupportedException(
                        $"Unsupported integration event type: {typeof(T).Name}")
            };
        }

        private static Guid? GetEventId<T>(
            T message)
            where T : class
        {
            return message switch
            {
                PaymentCompletedEvent completed =>
                    completed.EventId,

                PaymentFailedEvent failed =>
                    failed.EventId,

                _ =>
                    null
            };
        }

        private static string? GetCorrelationId<T>(
            T message)
            where T : class
        {
            return message switch
            {
                PaymentCompletedEvent completed =>
                    completed.CorrelationId,

                PaymentFailedEvent failed =>
                    failed.CorrelationId,

                _ =>
                    null
            };
        }
    }
}