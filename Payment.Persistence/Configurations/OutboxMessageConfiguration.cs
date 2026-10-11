using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Outbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Persistence.Configurations
{
    public sealed class OutboxMessageConfiguration
        : IEntityTypeConfiguration<OutboxMessage>
    {
        public void Configure(
            EntityTypeBuilder<OutboxMessage> builder)
        {
            builder.ToTable("OutboxMessages");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.EventType)
                .HasMaxLength(250)
                .IsRequired();

            builder.Property(x => x.Payload)
                .HasColumnType("nvarchar(max)")
                .IsRequired();

            builder.Property(x => x.LastError)
                .HasColumnType("nvarchar(max)");

            builder.Property(x => x.DeadLetterReason)
                .HasColumnType("nvarchar(max)");

            builder.HasIndex(x => x.ProcessedAtUtc);

            builder.HasIndex(x => x.NextAttemptAtUtc);

            builder.HasIndex(x => x.DeadLetteredAtUtc);
            builder.Ignore(x => x.IsProcessed);

            builder.Ignore(x => x.IsDeadLettered);
            builder.Property(x => x.LockedBy)
    .HasMaxLength(200);

            builder.HasIndex(x => x.LockedUntilUtc);
        }
    }
}
