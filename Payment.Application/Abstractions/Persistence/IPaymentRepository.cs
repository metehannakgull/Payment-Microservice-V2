using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PaymentEntity = Payment.Domain.Entities.Payment;

namespace Payment.Application.Abstractions.Persistence
{
    public interface IPaymentRepository
    {
        Task AddAsync(
            PaymentEntity payment,
            CancellationToken cancellationToken = default);

        Task<PaymentEntity?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default);

        Task<PaymentEntity?> GetByOrderIdAsync(
            string orderId,
            CancellationToken cancellationToken = default);

        Task UpdateAsync(
            PaymentEntity payment,
            CancellationToken cancellationToken = default);
    }
}
