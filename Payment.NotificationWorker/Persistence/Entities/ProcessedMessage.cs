using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.NotificationWorker.Persistence.Entities
{
    public sealed class ProcessedMessage
    {
        public Guid EventId { get; set; }

        public string ConsumerName { get; set; } =
            string.Empty;

        // 0 = Processing
        // 1 = Completed
        public int Status { get; set; }

        public DateTime ReservedAtUtc { get; set; }

        public string? LockedBy { get; set; }

        public DateTime? LockedUntilUtc { get; set; }

        public DateTime? ProcessedAtUtc { get; set; }

        public string? LastError { get; set; }
    }
}
