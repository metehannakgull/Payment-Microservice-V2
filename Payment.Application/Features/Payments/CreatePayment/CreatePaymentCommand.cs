using Payment.Domain.Enums.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Application.Features.Payments.CreatePayment
{
    public sealed class CreatePaymentCommand
    {
        public string OrderId { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public Currency Currency { get; set; }

        public PaymentProviderType Provider { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;
    }
}
