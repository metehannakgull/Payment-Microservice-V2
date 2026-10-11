using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Application.Abstractions.Outbox
{
    public sealed class OutboxMessage
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        public string EventType { get; init; } = string.Empty;

        public string Payload { get; init; } = string.Empty;

        public DateTime OccurredAtUtc { get; init; } =
            DateTime.UtcNow;

        public DateTime? ProcessedAtUtc { get; set; }

        public int RetryCount { get; set; }

        public string? LastError { get; set; }

        public bool IsProcessed =>
            ProcessedAtUtc.HasValue;
        public DateTime? NextAttemptAtUtc { get; set; }
        public DateTime? DeadLetteredAtUtc { get; set; }

        public string? DeadLetterReason { get; set; }

        public bool IsDeadLettered =>
            DeadLetteredAtUtc.HasValue;
        public string? LockedBy { get; set; }
        public DateTime? LockedUntilUtc { get; set; }
    }
}
