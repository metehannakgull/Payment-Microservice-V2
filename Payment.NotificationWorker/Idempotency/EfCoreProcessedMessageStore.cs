using Microsoft.EntityFrameworkCore;
using Payment.Messaging.Abstractions.Consuming;
using Payment.NotificationWorker.Persistence.Database;
using Payment.NotificationWorker.Persistence.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Payment.NotificationWorker.Idempotency
{
    public sealed class EfCoreProcessedMessageStore
       : IProcessedMessageStore
    {
        private const string ConsumerName =
            "Payment.NotificationWorker";

        private readonly NotificationDbContext _dbContext;

        public EfCoreProcessedMessageStore(
            NotificationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<ProcessedMessageReservationResult> TryReserveAsync(
            Guid eventId,
            string lockId,
            DateTime nowUtc,
            TimeSpan lockDuration,
            CancellationToken cancellationToken = default)
        {
            var lockUntilUtc =
                nowUtc.Add(lockDuration);

            // =====================================================
            // 1. ÖNCE KAYIT VAR MI BAK
            //
            // Bu sadece gereksiz duplicate INSERT exception'larını
            // azaltmak için.
            //
            // Concurrency güvenliğini tek başına bu SELECT sağlamaz.
            // =====================================================

            var existingMessage =
                await _dbContext.ProcessedMessages
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.EventId == eventId &&
                            x.ConsumerName == ConsumerName,
                        cancellationToken);

            // =====================================================
            // 2. KAYIT YOKSA İLK RESERVATION'I INSERT ET
            // =====================================================

            if (existingMessage is null)
            {
                var newMessage =
                    new ProcessedMessage
                    {
                        EventId = eventId,
                        ConsumerName = ConsumerName,

                        Status = 0,

                        ReservedAtUtc = nowUtc,

                        LockedBy = lockId,

                        LockedUntilUtc = lockUntilUtc,

                        ProcessedAtUtc = null,

                        LastError = null
                    };

                await _dbContext.ProcessedMessages.AddAsync(
                    newMessage,
                    cancellationToken);

                try
                {
                    await _dbContext.SaveChangesAsync(
                        cancellationToken);

                    return ProcessedMessageReservationResult.Acquired;
                }
                catch (DbUpdateException ex)
                    when (IsUniqueViolation(ex))
                {
                    // Başka worker SELECT ile INSERT arasındaki kısa
                    // zamanda aynı EventId'yi eklemiş olabilir.
                    //
                    // Bu normal concurrency yarışıdır.

                    _dbContext.Entry(newMessage)
                        .State = EntityState.Detached;

                    // Gerçek DB durumunu yeniden oku.
                    existingMessage =
                        await _dbContext.ProcessedMessages
                            .AsNoTracking()
                            .FirstOrDefaultAsync(
                                x =>
                                    x.EventId == eventId &&
                                    x.ConsumerName == ConsumerName,
                                cancellationToken);

                    if (existingMessage is null)
                    {
                        // PK violation aldık ama kayıt okunamıyorsa
                        // beklenmeyen bir durum var.
                        throw;
                    }
                }
            }

            // =====================================================
            // 3. EVENT DAHA ÖNCE TAMAMLANMIŞ
            // =====================================================

            if (existingMessage.Status == 1)
            {
                return ProcessedMessageReservationResult
                    .AlreadyProcessed;
            }

            // =====================================================
            // 4. AKTİF LEASE VAR
            //
            // Başka worker hâlâ bu event'in sahibi.
            // =====================================================

            if (existingMessage.LockedUntilUtc.HasValue &&
                existingMessage.LockedUntilUtc.Value > nowUtc)
            {
                return ProcessedMessageReservationResult
                    .InProgress;
            }

            // =====================================================
            // 5. LEASE EXPIRE OLMUŞ
            //
            // Event Processing durumda ama lock artık geçerli değil.
            // Atomik conditional UPDATE ile takeover deniyoruz.
            // =====================================================

            var affectedRows =
                await _dbContext.ProcessedMessages
                    .Where(x =>
                        x.EventId == eventId &&
                        x.ConsumerName == ConsumerName &&
                        x.Status == 0 &&
                        (
                            x.LockedUntilUtc == null ||
                            x.LockedUntilUtc <= nowUtc
                        ))
                    .ExecuteUpdateAsync(
                        setters => setters

                            .SetProperty(
                                x => x.LockedBy,
                                lockId)

                            .SetProperty(
                                x => x.LockedUntilUtc,
                                lockUntilUtc)

                            .SetProperty(
                                x => x.ReservedAtUtc,
                                nowUtc)

                            .SetProperty(
                                x => x.LastError,
                                (string?)null),

                        cancellationToken);

            if (affectedRows == 1)
            {
                return ProcessedMessageReservationResult.Acquired;
            }

            // Biz UPDATE yapana kadar başka worker event'i aldı.
            return ProcessedMessageReservationResult.InProgress;
        }
        private static bool IsUniqueViolation(DbUpdateException exception)
        {
            return exception.InnerException is SqlException sqlException
                && (sqlException.Number == 2601 ||
                    sqlException.Number == 2627);
        }

        public async Task<bool> RenewLeaseAsync(
                Guid eventId,
                string lockId,
                DateTime nowUtc,
                TimeSpan lockDuration,
                CancellationToken cancellationToken = default)
        {
            var newLockedUntilUtc =
                nowUtc.Add(lockDuration);

            var affectedRows =
                await _dbContext.ProcessedMessages
                    .Where(x =>
                        x.EventId == eventId &&
                        x.ConsumerName == ConsumerName &&
                        x.Status == 0 &&
                        x.LockedBy == lockId)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                x => x.LockedUntilUtc,
                                newLockedUntilUtc),
                        cancellationToken);

            return affectedRows == 1;
        }

        // =====================================================
        // MARK AS PROCESSED
        // =====================================================

        public async Task MarkAsProcessedAsync(
     Guid eventId,
     string lockId,
     CancellationToken cancellationToken = default)
        {
            var nowUtc =
                DateTime.UtcNow;

            var affectedRows =
                await _dbContext.ProcessedMessages
                    .Where(x =>
                        x.EventId == eventId &&
                        x.ConsumerName == ConsumerName &&
                        x.Status == 0 &&
                        x.LockedBy == lockId)
                    .ExecuteUpdateAsync(
                        setters => setters

                            .SetProperty(
                                x => x.Status,
                                1)

                            .SetProperty(
                                x => x.ProcessedAtUtc,
                                nowUtc)

                            .SetProperty(
                                x => x.LockedBy,
                                (string?)null)

                            .SetProperty(
                                x => x.LockedUntilUtc,
                                (DateTime?)null)

                            .SetProperty(
                                x => x.LastError,
                                (string?)null),

                        cancellationToken);

            if (affectedRows != 1)
            {
                throw new InvalidOperationException(
                    $"Inbox message could not be marked as processed. " +
                    $"EventId={eventId}, LockId={lockId}. " +
                    $"The lease may have been lost or the message may already be completed.");
            }
        }

        // =====================================================
        // RELEASE
        //
        // Handler hata verirse reservation'ı bırakıyoruz.
        // Status = 0 kalıyor.
        //
        // RabbitMQ retry ile event tekrar geldiğinde yeni worker
        // tekrar TryReserveAsync ile alabilir.
        // =====================================================

        public async Task ReleaseAsync(
            Guid eventId,
            string lockId,
            string? error = null,
            CancellationToken cancellationToken = default)
        {
            await _dbContext.ProcessedMessages
                .Where(x =>
                    x.EventId == eventId &&
                    x.ConsumerName == ConsumerName &&
                    x.Status == 0 &&
                    x.LockedBy == lockId)
                .ExecuteUpdateAsync(
                    setters => setters

                        .SetProperty(
                            x => x.LockedBy,
                            (string?)null)

                        .SetProperty(
                            x => x.LockedUntilUtc,
                            (DateTime?)null)

                        .SetProperty(
                            x => x.LastError,
                            error),

                    cancellationToken);
        }
    }
}
