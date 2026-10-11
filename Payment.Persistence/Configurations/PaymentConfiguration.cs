using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PaymentEntity = Payment.Domain.Entities.Payment;
namespace Payment.Persistence.Configurations
{
    public sealed class PaymentConfiguration
       : IEntityTypeConfiguration<PaymentEntity>
    {
        public void Configure(
            EntityTypeBuilder<PaymentEntity> builder)
        {
            builder.ToTable("Payments");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.OrderId)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.Amount)
                .HasPrecision(18, 2)
                .IsRequired();

            builder.Property(x => x.Currency)
                .IsRequired();

            builder.Property(x => x.Provider)
                .IsRequired();

            builder.Property(x => x.Status)
                .IsRequired();

            builder.Property(x => x.TransactionId)
                .HasMaxLength(200);

            builder.Property(x => x.ErrorCode)
                .HasMaxLength(100);

            builder.Property(x => x.ErrorMessage)
                .HasMaxLength(1000);

            builder.HasIndex(x => x.OrderId);
        }
    }
}
