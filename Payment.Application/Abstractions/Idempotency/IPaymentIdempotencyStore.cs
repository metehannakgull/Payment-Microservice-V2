using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Application.Abstractions.Idempotency
{
    public interface IPaymentIdempotencyStore
    {
        Task<PaymentIdempotencyRecord?> GetAsync(
            string key,
            CancellationToken cancellationToken = default);

        Task<bool> TryReserveAsync(
            string key,
            string requestHash,
            CancellationToken cancellationToken = default);

        Task CompleteAsync(
            string key,
            PaymentIdempotencyRecord record,
            CancellationToken cancellationToken = default);

        Task RemoveAsync(
            string key,
            CancellationToken cancellationToken = default);
    }
}
