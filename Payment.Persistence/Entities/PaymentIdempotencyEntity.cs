using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Persistence.Entities
{
    public sealed class PaymentIdempotencyEntity
    {
        public string Key { get; set; } = string.Empty;

        public string RequestHash { get; set; } = string.Empty;

        public int Status { get; set; }

        public string? ResultJson { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }
    }
}
