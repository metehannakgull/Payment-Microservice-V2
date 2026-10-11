using Payment.Application.Abstractions.Idempotency;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Persistence.Idempotency.InMemory
{
    public sealed class InMemoryPaymentIdempotencyStore
          : IPaymentIdempotencyStore
    {
        private readonly ConcurrentDictionary<
            string,
            PaymentIdempotencyRecord> _records = new();

        public Task<PaymentIdempotencyRecord?> GetAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            _records.TryGetValue(
                key,
                out var record);

            return Task.FromResult(record);
        }

        public Task<bool> TryReserveAsync(
            string key,
            string requestHash,
            CancellationToken cancellationToken = default)
        {
            var record = new PaymentIdempotencyRecord
            {
                Key = key,
                RequestHash = requestHash,
                Status = IdempotencyStatus.Processing,
                CreatedAtUtc = DateTime.UtcNow
            };

            var added = _records.TryAdd(
                key,
                record);

            return Task.FromResult(added);
        }

        public Task CompleteAsync(
            string key,
            PaymentIdempotencyRecord record,
            CancellationToken cancellationToken = default)
        {
            _records[key] = record;

            return Task.CompletedTask;
        }

        public Task RemoveAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            _records.TryRemove(
                key,
                out _);

            return Task.CompletedTask;
        }
    }
}
