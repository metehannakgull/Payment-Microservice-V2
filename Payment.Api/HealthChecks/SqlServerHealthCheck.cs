using Microsoft.Extensions.Diagnostics.HealthChecks;
using Payment.Persistence.Database;

namespace Payment.Api.HealthChecks
{
    public sealed class SqlServerHealthCheck : IHealthCheck
    {
        private readonly PaymentDbContext _dbContext;

        public SqlServerHealthCheck(
            PaymentDbContext dbContext)
        {
            _dbContext = dbContext;
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

                var canConnect =
                    await _dbContext.Database.CanConnectAsync(
                        timeoutCts.Token);

                if (!canConnect)
                {
                    return HealthCheckResult.Unhealthy(
                        "Payment database is unreachable.");
                }

                return HealthCheckResult.Healthy(
                    "Payment database is reachable.");
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                return HealthCheckResult.Unhealthy(
                    "Payment database health check timed out.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy(
                    "Payment database health check failed.",
                    ex);
            }
        }
    }
}
