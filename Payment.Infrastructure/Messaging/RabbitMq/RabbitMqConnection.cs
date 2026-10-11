using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Payment.Infrastructure.Messaging.RabbitMq
{
    public sealed class RabbitMqConnection : IAsyncDisposable
    {
        private readonly RabbitMqOptions _options;
        private readonly ILogger<RabbitMqConnection> _logger;

        private IConnection? _connection;

        private readonly SemaphoreSlim _lock =
            new(1, 1);

        public RabbitMqConnection(
            IOptions<RabbitMqOptions> options,
            ILogger<RabbitMqConnection> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task<IConnection> GetConnectionAsync(
            CancellationToken cancellationToken = default)
        {
            if (_connection is not null &&
                _connection.IsOpen)
            {
                return _connection;
            }

            await _lock.WaitAsync(
                cancellationToken);

            try
            {
                if (_connection is not null &&
                    _connection.IsOpen)
                {
                    return _connection;
                }

                _logger.LogInformation(
                    "Creating RabbitMQ connection. Host={Host}, Port={Port}, VirtualHost={VirtualHost}",
                    _options.HostName,
                    _options.Port,
                    _options.VirtualHost);

                var factory =
                    new ConnectionFactory
                    {
                        HostName =
                            _options.HostName,

                        Port =
                            _options.Port,

                        UserName =
                            _options.UserName,

                        Password =
                            _options.Password,

                        VirtualHost =
                            _options.VirtualHost,

                        ClientProvidedName =
                            "Payment.Api",

                        AutomaticRecoveryEnabled =
                            true,

                        TopologyRecoveryEnabled =
                            true,

                        NetworkRecoveryInterval =
                            TimeSpan.FromSeconds(5)
                    };

                _connection =
                    await factory.CreateConnectionAsync(
                        cancellationToken);

                _logger.LogInformation(
                    "RabbitMQ connection established. IsOpen={IsOpen}",
                    _connection.IsOpen);

                _connection.ConnectionShutdownAsync +=
                    async (_, args) =>
                    {
                        _logger.LogWarning(
                            "RabbitMQ connection shutdown. ReplyCode={ReplyCode}, ReplyText={ReplyText}, Initiator={Initiator}",
                            args.ReplyCode,
                            args.ReplyText,
                            args.Initiator);

                        await Task.CompletedTask;
                    };

                _connection.CallbackExceptionAsync +=
                    async (_, args) =>
                    {
                        _logger.LogError(
                            args.Exception,
                            "RabbitMQ connection callback exception.");

                        await Task.CompletedTask;
                    };

                _connection.ConnectionRecoveryErrorAsync +=
                    async (_, args) =>
                    {
                        _logger.LogError(
                            args.Exception,
                            "RabbitMQ connection recovery failed.");

                        await Task.CompletedTask;
                    };

                _connection.RecoverySucceededAsync +=
                    async (_, _) =>
                    {
                        _logger.LogInformation(
                            "RabbitMQ connection recovery succeeded.");

                        await Task.CompletedTask;
                    };

                return _connection;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            _lock.Dispose();
        }
    }
}