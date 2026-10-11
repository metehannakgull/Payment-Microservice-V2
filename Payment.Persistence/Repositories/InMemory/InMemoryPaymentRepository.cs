using Payment.Application.Abstractions.Persistence;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PaymentEntity = Payment.Domain.Entities.Payment;

namespace Payment.Persistence.Repositories.InMemory
{
    public class InMemoryPaymentRepository : IPaymentRepository
    {
        private static readonly ConcurrentDictionary<Guid, PaymentEntity> _payments = new();

        public Task AddAsync(
            PaymentEntity payment,
            CancellationToken cancellationToken = default)
        {
            _payments[payment.Id] = payment;

            return Task.CompletedTask;
        }

        public Task<PaymentEntity?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            _payments.TryGetValue(id, out var payment);

            return Task.FromResult(payment);
        }

        public Task<PaymentEntity?> GetByOrderIdAsync(
            string orderId,
            CancellationToken cancellationToken = default)
        {
            var payment = _payments.Values
                .FirstOrDefault(x => x.OrderId == orderId);

            return Task.FromResult(payment);
        }

        public Task UpdateAsync(
            PaymentEntity payment,
            CancellationToken cancellationToken = default)
        {
            _payments[payment.Id] = payment;

            return Task.CompletedTask;
        }
    }
}
