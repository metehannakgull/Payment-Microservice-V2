using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Idempotency;
using Payment.Application.Features.Payments.CreatePayment;
using Payment.Persistence.Database;
using Payment.Persistence.Entities;
using System.Text.Json;

namespace Payment.Persistence.Idempotency.EfCore
{
    public sealed class EfCorePaymentIdempotencyStore
        : IPaymentIdempotencyStore
    {
        private readonly PaymentDbContext _dbContext;

        public EfCorePaymentIdempotencyStore(
            PaymentDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<PaymentIdempotencyRecord?> GetAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            var entity =
                await _dbContext.PaymentIdempotencyRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.Key == key,
                        cancellationToken);

            if (entity is null)
                return null;

            CreatePaymentResult? result = null;

            if (!string.IsNullOrWhiteSpace(
                entity.ResultJson))
            {
                result =
                    JsonSerializer.Deserialize<CreatePaymentResult>(
                        entity.ResultJson);
            }

            return new PaymentIdempotencyRecord
            {
                Key = entity.Key,
                RequestHash = entity.RequestHash,
                Status =
                    (IdempotencyStatus)entity.Status,
                Result = result,
                CreatedAtUtc = entity.CreatedAtUtc,
                CompletedAtUtc = entity.CompletedAtUtc
            };
        }

        public async Task<bool> TryReserveAsync(
            string key,
            string requestHash,
            CancellationToken cancellationToken = default)
        {
            var entity =
                new PaymentIdempotencyEntity
                {
                    Key = key,
                    RequestHash = requestHash,
                    Status =
                        (int)IdempotencyStatus.Processing,
                    CreatedAtUtc = DateTime.UtcNow
                };

            await _dbContext
                .PaymentIdempotencyRecords
                .AddAsync(
                    entity,
                    cancellationToken);

            try
            {
                await _dbContext.SaveChangesAsync(
                    cancellationToken);

                return true;
            }
            catch (DbUpdateException)
            {
                _dbContext.Entry(entity).State =
                    EntityState.Detached;

                return false;
            }
        }

        public async Task CompleteAsync(
            string key,
            PaymentIdempotencyRecord record,
            CancellationToken cancellationToken = default)
        {
            var entity =
                await _dbContext
                    .PaymentIdempotencyRecords
                    .FirstOrDefaultAsync(
                        x => x.Key == key,
                        cancellationToken);

            if (entity is null)
            {
                throw new InvalidOperationException(
                    $"Idempotency record not found. Key: {key}");
            }

            entity.RequestHash =
                record.RequestHash;

            entity.Status =
                (int)record.Status;

            entity.ResultJson =
                record.Result is null
                    ? null
                    : JsonSerializer.Serialize(
                        record.Result);

            entity.CompletedAtUtc =
                record.CompletedAtUtc;

            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }

        public async Task RemoveAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            var entity =
                await _dbContext
                    .PaymentIdempotencyRecords
                    .FirstOrDefaultAsync(
                        x => x.Key == key,
                        cancellationToken);

            if (entity is null)
                return;

            _dbContext
                .PaymentIdempotencyRecords
                .Remove(entity);

            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
    }
}