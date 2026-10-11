using Payment.Application.Features.Payments.CreatePayment;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Application.Abstractions.Idempotency
{
    public sealed class PaymentIdempotencyRecord
    {
        public string Key { get; init; } = string.Empty;

        public string RequestHash { get; init; } = string.Empty;

        public IdempotencyStatus Status { get; set; }

        public CreatePaymentResult? Result { get; set; }

        public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

        public DateTime? CompletedAtUtc { get; set; }
    }
}
