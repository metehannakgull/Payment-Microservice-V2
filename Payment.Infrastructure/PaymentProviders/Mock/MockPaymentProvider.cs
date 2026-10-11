using Payment.Application.Abstractions.Payments;
using Payment.Application.Models;
using Payment.Domain.Enums.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Infrastructure.PaymentProviders.Mock
{
    public class MockPaymentProvider : IPaymentProvider
    {
        public PaymentProviderType ProviderType => PaymentProviderType.Mock;

        public async Task<PaymentResult> PayAsync(
            PaymentRequest request,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(300, cancellationToken);

            return new PaymentResult
            {
                IsSuccess = true,
                TransactionId = Guid.NewGuid().ToString(),
                ErrorCode = null,
                ErrorMessage = null,
                ThreeDSecureUrl = null
            };
        }
    }
}
