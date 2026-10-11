using Microsoft.EntityFrameworkCore;
using Payment.NotificationWorker.Persistence.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.NotificationWorker.Persistence.Database
{
    public sealed class NotificationDbContext : DbContext
    {
        public NotificationDbContext(
            DbContextOptions<NotificationDbContext> options)
            : base(options)
        {
        }

        public DbSet<ProcessedMessage> ProcessedMessages =>
            Set<ProcessedMessage>();

        protected override void OnModelCreating(
     ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProcessedMessage>(
                entity =>
                {
                    entity.ToTable("ProcessedMessages");

                    entity.HasKey(x =>
                        new
                        {
                            x.EventId,
                            x.ConsumerName
                        });

                    entity.Property(x => x.ConsumerName)
                        .HasMaxLength(200)
                        .IsRequired();

                    entity.Property(x => x.Status)
                        .IsRequired();

                    entity.Property(x => x.ReservedAtUtc)
                        .IsRequired();

                    entity.Property(x => x.LockedBy)
                        .HasMaxLength(200);

                    entity.Property(x => x.LastError)
                        .HasMaxLength(2000);

                    entity.HasIndex(x =>
                        new
                        {
                            x.Status,
                            x.LockedUntilUtc
                        });

                    entity.HasIndex(x => x.ProcessedAtUtc);
                });
        }
    }
}
