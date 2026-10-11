using Payment.Messaging.Abstractions.Consuming;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.NotificationWorker.Idempotency
{
    public sealed class InMemoryProcessedMessageStore
        : IProcessedMessageStore
    {
        private sealed class ReservationState
        {
            public int Status { get; set; }

            public string? LockedBy { get; set; }

            public DateTime? LockedUntilUtc { get; set; }

            public DateTime? ProcessedAtUtc { get; set; }

            public string? LastError { get; set; }
        }

        private readonly ConcurrentDictionary<Guid, ReservationState>
            _messages = new();

        private readonly object _lock = new();

        public Task<ProcessedMessageReservationResult> TryReserveAsync(
            Guid eventId,
            string lockId,
            DateTime nowUtc,
            TimeSpan lockDuration,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (!_messages.TryGetValue(
                    eventId,
                    out var existing))
                {
                    _messages[eventId] =
                        new ReservationState
                        {
                            Status = 0,
                            LockedBy = lockId,
                            LockedUntilUtc =
                                nowUtc.Add(lockDuration),
                            ProcessedAtUtc = null,
                            LastError = null
                        };

                    return Task.FromResult(
                        ProcessedMessageReservationResult.Acquired);
                }

                if (existing.Status == 1)
                {
                    return Task.FromResult(
                        ProcessedMessageReservationResult
                            .AlreadyProcessed);
                }

                if (existing.LockedUntilUtc.HasValue &&
                    existing.LockedUntilUtc.Value > nowUtc)
                {
                    return Task.FromResult(
                        ProcessedMessageReservationResult
                            .InProgress);
                }

                existing.LockedBy =
                    lockId;

                existing.LockedUntilUtc =
                    nowUtc.Add(lockDuration);

                existing.LastError =
                    null;

                return Task.FromResult(
                    ProcessedMessageReservationResult.Acquired);
            }
        }

        public Task MarkAsProcessedAsync(
            Guid eventId,
            string lockId,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (!_messages.TryGetValue(
                    eventId,
                    out var existing))
                {
                    return Task.CompletedTask;
                }

                if (existing.Status != 0 ||
                    existing.LockedBy != lockId)
                {
                    return Task.CompletedTask;
                }

                existing.Status = 1;

                existing.ProcessedAtUtc =
                    DateTime.UtcNow;

                existing.LockedBy =
                    null;

                existing.LockedUntilUtc =
                    null;

                existing.LastError =
                    null;

                return Task.CompletedTask;
            }
        }

        public Task ReleaseAsync(
            Guid eventId,
            string lockId,
            string? error = null,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (!_messages.TryGetValue(
                    eventId,
                    out var existing))
                {
                    return Task.CompletedTask;
                }

                if (existing.Status != 0 ||
                    existing.LockedBy != lockId)
                {
                    return Task.CompletedTask;
                }

                existing.LockedBy =
                    null;

                existing.LockedUntilUtc =
                    null;

                existing.LastError =
                    error;

                return Task.CompletedTask;
            }
        }

        public Task<bool> RenewLeaseAsync(
            Guid eventId,
            string lockId,
            DateTime nowUtc,
            TimeSpan lockDuration,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (!_messages.TryGetValue(
                    eventId,
                    out var existing))
                {
                    return Task.FromResult(false);
                }

                if (existing.Status != 0 ||
                    existing.LockedBy != lockId)
                {
                    return Task.FromResult(false);
                }

                existing.LockedUntilUtc =
                    nowUtc.Add(lockDuration);

                return Task.FromResult(true);
            }
        }
    }
}
