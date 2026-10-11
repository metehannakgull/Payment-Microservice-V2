using Payment.Application.Abstractions.Persistence;
using Payment.Persistence.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PaymentEntity = Payment.Domain.Entities.Payment;

namespace Payment.Persistence.Repositories.EfCore
{
    public sealed class EfCorePaymentRepository : IPaymentRepository
    {
        private readonly PaymentDbContext _dbContext;

        public EfCorePaymentRepository(
            PaymentDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task AddAsync(
            PaymentEntity payment,
            CancellationToken cancellationToken = default)
        {
            await _dbContext.Payments.AddAsync(
                payment,
                cancellationToken);

            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }

        public async Task UpdateAsync(
            PaymentEntity payment,
            CancellationToken cancellationToken = default)
        {
            _dbContext.Payments.Update(payment);

            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }

        public async Task<PaymentEntity?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.Payments
                .FirstOrDefaultAsync(
                    x => x.Id == id,
                    cancellationToken);
        }

        public async Task<PaymentEntity?> GetByOrderIdAsync(
            string orderId,
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.Payments
                .FirstOrDefaultAsync(
                    x => x.OrderId == orderId,
                    cancellationToken);
        }
    }
}
