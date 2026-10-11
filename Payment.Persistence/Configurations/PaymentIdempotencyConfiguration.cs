using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Payment.Persistence.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Persistence.Configurations
{
    public sealed class PaymentIdempotencyConfiguration
       : IEntityTypeConfiguration<PaymentIdempotencyEntity>
    {
        public void Configure(
            EntityTypeBuilder<PaymentIdempotencyEntity> builder)
        {
            builder.ToTable("PaymentIdempotency");

            builder.HasKey(x => x.Key);

            builder.Property(x => x.Key)
                .HasMaxLength(200);

            builder.Property(x => x.RequestHash)
                .HasMaxLength(128)
                .IsRequired();

            builder.Property(x => x.ResultJson)
                .HasColumnType("nvarchar(max)");

            builder.HasIndex(x => x.RequestHash);
        }
    }
}
