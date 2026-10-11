using Payment.Application.Models;
using Payment.Domain.Enums.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Application.Abstractions.Payments
{
    public interface IPaymentProvider
    {
        PaymentProviderType ProviderType { get; }

        Task<PaymentResult> PayAsync(
            PaymentRequest request,
            CancellationToken cancellationToken = default);
    }
}
