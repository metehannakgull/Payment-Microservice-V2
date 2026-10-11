using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Outbox;
using Payment.Persistence.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Persistence.Database
{
    public sealed class PaymentDbContext : DbContext
    {
        public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options)
        {
        }

        public DbSet<Payment.Domain.Entities.Payment> Payments =>
            Set<Payment.Domain.Entities.Payment>();

        public DbSet<OutboxMessage> OutboxMessages =>
            Set<OutboxMessage>();

        public DbSet<PaymentIdempotencyEntity> PaymentIdempotencyRecords =>
            Set<PaymentIdempotencyEntity>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfigurationsFromAssembly(
                typeof(PaymentDbContext).Assembly);
        }
    }
}
