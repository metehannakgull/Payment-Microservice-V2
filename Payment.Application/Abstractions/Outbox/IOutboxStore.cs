using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Application.Abstractions.Outbox
{
    public interface IOutboxStore
    {
        Task AddAsync(
            OutboxMessage message,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyCollection<OutboxMessage>> GetPendingAsync(
            int batchSize,
            CancellationToken cancellationToken = default);
        Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
            string lockId,
            DateTime nowUtc,
            TimeSpan lockDuration,
            int batchSize,
            CancellationToken cancellationToken = default);

        Task MarkAsProcessedAsync(
     Guid messageId,
     string lockId,
     CancellationToken cancellationToken = default);

        Task MarkAsFailedAsync(
            Guid messageId,
            string lockId,
            string error,
            CancellationToken cancellationToken = default);

        Task MarkAsDeadLetterAsync(
            Guid messageId,
            string lockId,
            string reason,
            CancellationToken cancellationToken = default);
        Task<bool> RenewLockAsync(
            Guid messageId,
            string lockId,
            DateTime newLockedUntilUtc,
            CancellationToken cancellationToken = default);
        Task<int> CountPendingAsync(
            CancellationToken cancellationToken = default);
    }
}
