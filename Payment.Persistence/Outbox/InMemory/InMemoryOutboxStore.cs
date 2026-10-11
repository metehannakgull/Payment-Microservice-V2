using Payment.Application.Abstractions.Outbox;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Persistence.Outbox.InMemory
{
    public sealed class InMemoryOutboxStore : IOutboxStore
    {
        private readonly ConcurrentDictionary<Guid, OutboxMessage> _messages =
            new();

        private readonly object _lock = new();

        public Task AddAsync(
            OutboxMessage message,
            CancellationToken cancellationToken = default)
        {
            _messages[message.Id] = message;

            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<OutboxMessage>> GetPendingAsync(
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            var nowUtc = DateTime.UtcNow;

            var messages =
                _messages.Values
                    .Where(x =>
                        !x.ProcessedAtUtc.HasValue &&
                        !x.DeadLetteredAtUtc.HasValue &&
                        (
                            x.NextAttemptAtUtc == null ||
                            x.NextAttemptAtUtc <= nowUtc
                        ) &&
                        (
                            x.LockedUntilUtc == null ||
                            x.LockedUntilUtc <= nowUtc
                        ))
                    .OrderBy(x => x.OccurredAtUtc)
                    .Take(batchSize)
                    .ToList();

            return Task.FromResult<
                IReadOnlyCollection<OutboxMessage>>(
                    messages);
        }

        public Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
            string lockId,
            DateTime nowUtc,
            TimeSpan lockDuration,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var lockUntilUtc =
                    nowUtc.Add(lockDuration);

                var messages =
                    _messages.Values
                        .Where(x =>
                            !x.ProcessedAtUtc.HasValue &&
                            !x.DeadLetteredAtUtc.HasValue &&
                            (
                                x.NextAttemptAtUtc == null ||
                                x.NextAttemptAtUtc <= nowUtc
                            ) &&
                            (
                                x.LockedUntilUtc == null ||
                                x.LockedUntilUtc <= nowUtc
                            ))
                        .OrderBy(x => x.OccurredAtUtc)
                        .Take(batchSize)
                        .ToList();

                foreach (var message in messages)
                {
                    message.LockedBy =
                        lockId;

                    message.LockedUntilUtc =
                        lockUntilUtc;
                }

                return Task.FromResult<
                    IReadOnlyList<OutboxMessage>>(
                        messages);
            }
        }

        public Task MarkAsProcessedAsync(
    Guid messageId,
    string lockId,
    CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (!_messages.TryGetValue(
                    messageId,
                    out var message))
                {
                    throw new InvalidOperationException(
                        $"Outbox message not found. MessageId={messageId}");
                }

                if (message.LockedBy != lockId)
                {
                    throw new OutboxOwnershipLostException(
    messageId,
    lockId);
                }

                if (message.ProcessedAtUtc.HasValue ||
                    message.DeadLetteredAtUtc.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Outbox message is already finalized. MessageId={messageId}");
                }

                message.ProcessedAtUtc =
                    DateTime.UtcNow;

                message.LastError =
                    null;

                message.NextAttemptAtUtc =
                    null;

                message.LockedBy =
                    null;

                message.LockedUntilUtc =
                    null;
            }

            return Task.CompletedTask;
        }

        public Task MarkAsFailedAsync(
    Guid messageId,
    string lockId,
    string error,
    CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (!_messages.TryGetValue(
                    messageId,
                    out var message))
                {
                    throw new InvalidOperationException(
                        $"Outbox message not found. MessageId={messageId}");
                }

                if (message.LockedBy != lockId)
                {
                    throw new OutboxOwnershipLostException(
     messageId,
     lockId);
                }

                if (message.ProcessedAtUtc.HasValue ||
                    message.DeadLetteredAtUtc.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Outbox message is already finalized. MessageId={messageId}");
                }

                message.RetryCount++;

                message.LastError =
                    error;

                var delay =
                    GetRetryDelay(
                        message.RetryCount);

                message.NextAttemptAtUtc =
                    DateTime.UtcNow.Add(delay);

                message.LockedBy =
                    null;

                message.LockedUntilUtc =
                    null;
            }

            return Task.CompletedTask;
        }

        public Task MarkAsDeadLetterAsync(
    Guid messageId,
    string lockId,
    string reason,
    CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (!_messages.TryGetValue(
                    messageId,
                    out var message))
                {
                    throw new InvalidOperationException(
                        $"Outbox message not found. MessageId={messageId}");
                }

                if (message.LockedBy != lockId)
                {
                    throw new OutboxOwnershipLostException(
    messageId,
    lockId);
                }

                if (message.ProcessedAtUtc.HasValue ||
                    message.DeadLetteredAtUtc.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Outbox message is already finalized. MessageId={messageId}");
                }

                message.DeadLetteredAtUtc =
                    DateTime.UtcNow;

                message.DeadLetterReason =
                    reason;

                message.NextAttemptAtUtc =
                    null;

                message.LockedBy =
                    null;

                message.LockedUntilUtc =
                    null;
            }

            return Task.CompletedTask;
        }

        public Task<bool> RenewLockAsync(
    Guid messageId,
    string lockId,
    DateTime newLockedUntilUtc,
    CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (!_messages.TryGetValue(
                    messageId,
                    out var message))
                {
                    return Task.FromResult(false);
                }

                if (message.ProcessedAtUtc.HasValue ||
                    message.DeadLetteredAtUtc.HasValue)
                {
                    return Task.FromResult(false);
                }

                if (message.LockedBy != lockId)
                {
                    return Task.FromResult(false);
                }

                message.LockedUntilUtc =
                    newLockedUntilUtc;

                return Task.FromResult(true);
            }
        }

        public Task<int> CountPendingAsync(
    CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                var count =
                    _messages.Values.Count(x =>
                        !x.ProcessedAtUtc.HasValue &&
                        !x.DeadLetteredAtUtc.HasValue);

                return Task.FromResult(
                    count);
            }
        }
        private static TimeSpan GetRetryDelay(
    int retryCount)
        {
            var baseDelay = retryCount switch
            {
                <= 1 => TimeSpan.FromSeconds(2),
                2 => TimeSpan.FromSeconds(5),
                3 => TimeSpan.FromSeconds(15),
                4 => TimeSpan.FromSeconds(30),
                5 => TimeSpan.FromMinutes(1),
                6 => TimeSpan.FromMinutes(2),
                7 => TimeSpan.FromMinutes(5),
                _ => TimeSpan.FromMinutes(10)
            };

            var jitterSeconds =
                Random.Shared.Next(0, 6);

            return baseDelay.Add(
                TimeSpan.FromSeconds(jitterSeconds));
        }
    }
}
