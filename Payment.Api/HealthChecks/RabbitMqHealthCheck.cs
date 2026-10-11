using Microsoft.Extensions.Diagnostics.HealthChecks;
using Payment.Infrastructure.Messaging.RabbitMq;

namespace Payment.Api.HealthChecks
{
    public sealed class RabbitMqHealthCheck : IHealthCheck
    {
        private readonly RabbitMqConnection _connection;

        public RabbitMqHealthCheck(
            RabbitMqConnection connection)
        {
            _connection = connection;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var timeoutCts =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);

                timeoutCts.CancelAfter(
                    TimeSpan.FromSeconds(3));

                var connection =
                    await _connection.GetConnectionAsync(
                        timeoutCts.Token);

                if (!connection.IsOpen)
                {
                    return HealthCheckResult.Unhealthy(
                        "RabbitMQ connection is not open.");
                }

                return HealthCheckResult.Healthy(
                    "RabbitMQ is reachable.");
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                return HealthCheckResult.Unhealthy(
                    "RabbitMQ health check timed out.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy(
                    "RabbitMQ health check failed.",
                    ex);
            }
        }
    }
}
