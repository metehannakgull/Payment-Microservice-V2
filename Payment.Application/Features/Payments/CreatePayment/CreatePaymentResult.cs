using Payment.Domain.Enums.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Application.Features.Payments.CreatePayment
{
    public sealed class CreatePaymentResult
    {
        public Guid PaymentId { get; set; }

        public string OrderId { get; set; } = string.Empty;

        public PaymentStatus Status { get; set; }

        public string? TransactionId { get; set; }

        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
