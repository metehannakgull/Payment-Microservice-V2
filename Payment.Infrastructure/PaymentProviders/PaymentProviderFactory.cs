using Payment.Application.Abstractions.Payments;
using Payment.Domain.Enums.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Infrastructure.PaymentProviders
{
    public class PaymentProviderFactory : IPaymentProviderFactory
    {
        private readonly IEnumerable<IPaymentProvider> _paymentProviders;

        public PaymentProviderFactory(
            IEnumerable<IPaymentProvider> paymentProviders)
        {
            _paymentProviders = paymentProviders;
        }

        public IPaymentProvider GetProvider(
            PaymentProviderType providerType)
        {
            var provider = _paymentProviders
                .FirstOrDefault(x => x.ProviderType == providerType);

            if (provider == null)
            {
                throw new NotSupportedException(
                    $"Payment provider '{providerType}' is not supported.");
            }

            return provider;
        }
    }
}
