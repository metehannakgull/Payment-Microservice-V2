using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Payment.NotificationWorker.Messaging.RabbitMq
{
    public sealed class RabbitMqTopologyInitializer : IHostedService
    {
        private readonly RabbitMqOptions _options;
        private readonly ILogger<RabbitMqTopologyInitializer> _logger;

        public RabbitMqTopologyInitializer(
            IOptions<RabbitMqOptions> options,
            ILogger<RabbitMqTopologyInitializer> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task StartAsync(
            CancellationToken cancellationToken)
        {
            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                ClientProvidedName =
                    "Payment.NotificationWorker.Topology",

                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true
            };

            var attempt = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var connection =
                        await factory.CreateConnectionAsync(
                            cancellationToken);

                    await using var channel =
                        await connection.CreateChannelAsync(
                            cancellationToken: cancellationToken);

                    // =====================================================
                    // 1. MAIN PAYMENT EXCHANGE
                    // =====================================================

                    await channel.ExchangeDeclareAsync(
                        exchange: _options.ExchangeName,
                        type: ExchangeType.Topic,
                        durable: true,
                        autoDelete: false,
                        cancellationToken: cancellationToken);

                    // =====================================================
                    // 2. RETRY EXCHANGE
                    // =====================================================

                    await channel.ExchangeDeclareAsync(
                        exchange: _options.RetryExchangeName,
                        type: ExchangeType.Direct,
                        durable: true,
                        autoDelete: false,
                        cancellationToken: cancellationToken);

                    // =====================================================
                    // 3. DEAD LETTER EXCHANGE
                    // =====================================================

                    await channel.ExchangeDeclareAsync(
                        exchange: _options.DeadLetterExchangeName,
                        type: ExchangeType.Direct,
                        durable: true,
                        autoDelete: false,
                        cancellationToken: cancellationToken);

                    // =====================================================
                    // 4. DEAD LETTER QUEUE
                    // =====================================================

                    await channel.QueueDeclareAsync(
                        queue: _options.DeadLetterQueueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        cancellationToken: cancellationToken);

                    await channel.QueueBindAsync(
                        queue: _options.DeadLetterQueueName,
                        exchange: _options.DeadLetterExchangeName,
                        routingKey: _options.DeadLetterRoutingKey,
                        cancellationToken: cancellationToken);

                    // =====================================================
                    // 5. RETRY QUEUE
                    // =====================================================

                    var retryQueueArguments =
                        new Dictionary<string, object?>
                        {
                            ["x-message-ttl"] =
                                _options.RetryDelayMilliseconds,

                            ["x-dead-letter-exchange"] =
                                _options.ExchangeName,

                            ["x-dead-letter-routing-key"] =
                                _options.RoutingKey
                        };

                    await channel.QueueDeclareAsync(
                        queue: _options.RetryQueueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        arguments: retryQueueArguments,
                        cancellationToken: cancellationToken);

                    await channel.QueueBindAsync(
                        queue: _options.RetryQueueName,
                        exchange: _options.RetryExchangeName,
                        routingKey: _options.RetryRoutingKey,
                        cancellationToken: cancellationToken);

                    // =====================================================
                    // 6. MAIN CONSUMER QUEUE
                    // =====================================================

                    var mainQueueArguments =
                        new Dictionary<string, object?>
                        {
                            ["x-dead-letter-exchange"] =
                                _options.DeadLetterExchangeName,

                            ["x-dead-letter-routing-key"] =
                                _options.DeadLetterRoutingKey
                        };

                    await channel.QueueDeclareAsync(
                        queue: _options.QueueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        arguments: mainQueueArguments,
                        cancellationToken: cancellationToken);

                    await channel.QueueBindAsync(
                        queue: _options.QueueName,
                        exchange: _options.ExchangeName,
                        routingKey: _options.RoutingKey,
                        cancellationToken: cancellationToken);

                    _logger.LogInformation(
                        "RabbitMQ consumer topology initialized. Main={MainQueue}, Retry={RetryQueue}, DLQ={DeadLetterQueue}",
                        _options.QueueName,
                        _options.RetryQueueName,
                        _options.DeadLetterQueueName);

                    return;
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    attempt++;

                    var delay =
                        GetRetryDelay(attempt);

                    _logger.LogWarning(
                    "RabbitMQ unavailable. Retry attempt {Attempt} in {DelaySeconds} seconds. Error={Error}",
                    attempt,
                    delay.TotalSeconds,
                    ex.Message);

                    await Task.Delay(
                        delay,
                        cancellationToken);
                }
            }
        }

        public Task StopAsync(
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private static TimeSpan GetRetryDelay(
            int attempt)
        {
            return attempt switch
            {
                1 => TimeSpan.FromSeconds(2),
                2 => TimeSpan.FromSeconds(5),
                3 => TimeSpan.FromSeconds(10),
                4 => TimeSpan.FromSeconds(30),
                _ => TimeSpan.FromMinutes(1)
            };
        }
    }
}