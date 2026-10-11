using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Payment.Infrastructure.Messaging.RabbitMq
{
    public sealed class RabbitMqTopologyInitializer
        : BackgroundService
    {
        private readonly RabbitMqConnection _connection;
        private readonly RabbitMqOptions _options;
        private readonly ILogger<RabbitMqTopologyInitializer> _logger;

        public RabbitMqTopologyInitializer(
            RabbitMqConnection connection,
            IOptions<RabbitMqOptions> options,
            ILogger<RabbitMqTopologyInitializer> logger)
        {
            _connection = connection;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            var attempt = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var connection =
                        await _connection.GetConnectionAsync(
                            stoppingToken);

                    await using var channel =
                        await connection.CreateChannelAsync(
                            cancellationToken: stoppingToken);

                    await channel.ExchangeDeclareAsync(
                        exchange: _options.ExchangeName,
                        type: ExchangeType.Topic,
                        durable: true,
                        autoDelete: false,
                        cancellationToken: stoppingToken);

                    _logger.LogInformation(
                        "RabbitMQ producer exchange initialized. Exchange={ExchangeName}",
                        _options.ExchangeName);

                    return;
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    attempt++;

                    var delay =
                        GetRetryDelay(attempt);

                    _logger.LogWarning(
                     "RabbitMQ unavailable for producer topology. Retry attempt {Attempt} in {DelaySeconds} seconds. Error={Error}",
                     attempt,
                     delay.TotalSeconds,
                     ex.Message);

                    await Task.Delay(
                        delay,
                        stoppingToken);
                }
            }
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