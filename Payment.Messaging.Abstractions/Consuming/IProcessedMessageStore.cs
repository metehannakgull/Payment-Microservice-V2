using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Messaging.Abstractions.Consuming
{
    public interface IProcessedMessageStore
    {
        Task<ProcessedMessageReservationResult> TryReserveAsync(
            Guid eventId,
            string lockId,
            DateTime nowUtc,
            TimeSpan lockDuration,
            CancellationToken cancellationToken = default);

        Task MarkAsProcessedAsync(
            Guid eventId,
            string lockId,
            CancellationToken cancellationToken = default);

        Task ReleaseAsync(
            Guid eventId,
            string lockId,
            string? error = null,
            CancellationToken cancellationToken = default);
        Task<bool> RenewLeaseAsync(
            Guid eventId,
            string lockId,
            DateTime nowUtc,
            TimeSpan lockDuration,
            CancellationToken cancellationToken = default);
    }
}
