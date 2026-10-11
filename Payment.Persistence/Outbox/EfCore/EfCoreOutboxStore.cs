using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Outbox;
using Payment.Persistence.Database;
using Microsoft.Data.SqlClient;
using System.Data;
namespace Payment.Persistence.Outbox.EfCore
{
    public sealed class EfCoreOutboxStore : IOutboxStore
    {
        private readonly PaymentDbContext _dbContext;

        public EfCoreOutboxStore(
            PaymentDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // =====================================================
        // ADD
        // =====================================================

        public async Task AddAsync(
            OutboxMessage message,
            CancellationToken cancellationToken = default)
        {
            await _dbContext.OutboxMessages.AddAsync(
                message,
                cancellationToken);

            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }

        // =====================================================
        // GET PENDING
        // Eski yapı için şimdilik tutuyoruz.
        // Daha sonra worker ClaimPendingAsync kullanacak.
        // =====================================================

        public async Task<IReadOnlyCollection<OutboxMessage>> GetPendingAsync(
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            var nowUtc = DateTime.UtcNow;

            var messages =
                await _dbContext.OutboxMessages
                    .AsNoTracking()
                    .Where(x =>
                        !x.ProcessedAtUtc.HasValue &&
                        !x.DeadLetteredAtUtc.HasValue &&
                        (
                            x.NextAttemptAtUtc == null ||
                            x.NextAttemptAtUtc <= nowUtc
                        ) &&
                        (
                            x.LockedUntilUtc == null ||
                            x.LockedUntilUtc <= nowUtc
                        ))
                    .OrderBy(x => x.OccurredAtUtc)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken);

            return messages;
        }

        // =====================================================
        // CLAIM PENDING
        // Bir Outbox batch'ini belirli bir worker/instance için
        // geçici olarak kilitler.
        // =====================================================

        public async Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
     string lockId,
     DateTime nowUtc,
     TimeSpan lockDuration,
     int batchSize,
     CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(lockId))
            {
                throw new ArgumentException(
                    "LockId cannot be empty.",
                    nameof(lockId));
            }

            if (batchSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(batchSize),
                    "Batch size must be greater than zero.");
            }

            if (lockDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lockDuration),
                    "Lock duration must be greater than zero.");
            }

            // =====================================================
            // SQL SERVER ATOMIC OUTBOX CLAIM
            //
            // Tek SQL statement:
            //
            // 1. Claim edilebilir satırları bulur.
            // 2. Satırları update-lock ile korur.
            // 3. LockedBy / LockedUntilUtc değerlerini yazar.
            // 4. OUTPUT ile SADECE gerçekten claim edilen kayıtları döner.
            //
            // Böylece eski:
            //
            // SELECT -> UPDATE -> SELECT
            //
            // yarış penceresi ortadan kalkar.
            // =====================================================

            if (!_dbContext.Database.IsSqlServer())
            {
                throw new NotSupportedException(
                    "Atomic Outbox claiming is currently implemented for SQL Server only. " +
                    "PostgreSQL/MySQL providers will use their own provider-specific atomic claim strategies.");
            }

            var lockUntilUtc =
                nowUtc.Add(lockDuration);

            const string sql = """
        ;WITH ClaimableMessages AS
        (
            SELECT TOP (@BatchSize) *
            FROM dbo.OutboxMessages
                WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE
                ProcessedAtUtc IS NULL
                AND DeadLetteredAtUtc IS NULL
                AND
                (
                    NextAttemptAtUtc IS NULL
                    OR NextAttemptAtUtc <= @NowUtc
                )
                AND
                (
                    LockedUntilUtc IS NULL
                    OR LockedUntilUtc <= @NowUtc
                )
            ORDER BY OccurredAtUtc ASC
        )
        UPDATE ClaimableMessages
        SET
            LockedBy = @LockId,
            LockedUntilUtc = @LockUntilUtc
        OUTPUT
            INSERTED.Id,
            INSERTED.EventType,
            INSERTED.Payload,
            INSERTED.OccurredAtUtc,
            INSERTED.ProcessedAtUtc,
            INSERTED.RetryCount,
            INSERTED.LastError,
            INSERTED.NextAttemptAtUtc,
            INSERTED.DeadLetteredAtUtc,
            INSERTED.DeadLetterReason,
            INSERTED.LockedBy,
            INSERTED.LockedUntilUtc;
        """;

            var connection =
                _dbContext.Database.GetDbConnection();

            var shouldCloseConnection =
                connection.State != ConnectionState.Open;

            if (shouldCloseConnection)
            {
                await connection.OpenAsync(
                    cancellationToken);
            }

            try
            {
                await using var command =
                    connection.CreateCommand();

                command.CommandText = sql;
                command.CommandType = CommandType.Text;

                command.Parameters.Add(
                    new SqlParameter(
                        "@BatchSize",
                        SqlDbType.Int)
                    {
                        Value = batchSize
                    });

                command.Parameters.Add(
                    new SqlParameter(
                        "@NowUtc",
                        SqlDbType.DateTime2)
                    {
                        Value = nowUtc
                    });

                command.Parameters.Add(
                    new SqlParameter(
                        "@LockId",
                        SqlDbType.NVarChar,
                        200)
                    {
                        Value = lockId
                    });

                command.Parameters.Add(
                    new SqlParameter(
                        "@LockUntilUtc",
                        SqlDbType.DateTime2)
                    {
                        Value = lockUntilUtc
                    });

                var claimedMessages =
                    new List<OutboxMessage>();

                await using var reader =
                    await command.ExecuteReaderAsync(
                        cancellationToken);

                while (await reader.ReadAsync(
                    cancellationToken))
                {
                    claimedMessages.Add(
                        new OutboxMessage
                        {
                            Id =
                                reader.GetGuid(0),

                            EventType =
                                reader.GetString(1),

                            Payload =
                                reader.GetString(2),

                            OccurredAtUtc =
                                reader.GetDateTime(3),

                            ProcessedAtUtc =
                                reader.IsDBNull(4)
                                    ? null
                                    : reader.GetDateTime(4),

                            RetryCount =
                                reader.GetInt32(5),

                            LastError =
                                reader.IsDBNull(6)
                                    ? null
                                    : reader.GetString(6),

                            NextAttemptAtUtc =
                                reader.IsDBNull(7)
                                    ? null
                                    : reader.GetDateTime(7),

                            DeadLetteredAtUtc =
                                reader.IsDBNull(8)
                                    ? null
                                    : reader.GetDateTime(8),

                            DeadLetterReason =
                                reader.IsDBNull(9)
                                    ? null
                                    : reader.GetString(9),

                            LockedBy =
                                reader.IsDBNull(10)
                                    ? null
                                    : reader.GetString(10),

                            LockedUntilUtc =
                                reader.IsDBNull(11)
                                    ? null
                                    : reader.GetDateTime(11)
                        });
                }

                return claimedMessages;
            }
            finally
            {
                if (shouldCloseConnection)
                {
                    await connection.CloseAsync();
                }
            }
        }

        // =====================================================
        // MARK AS PROCESSED
        // =====================================================

        public async Task MarkAsProcessedAsync(
                Guid messageId,
                string lockId,
                CancellationToken cancellationToken = default)
            {
            var nowUtc =
                DateTime.UtcNow;

            var affectedRows =
                await _dbContext.OutboxMessages
                    .Where(x =>
                        x.Id == messageId &&
                        x.ProcessedAtUtc == null &&
                        x.DeadLetteredAtUtc == null &&
                        x.LockedBy == lockId)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                x => x.ProcessedAtUtc,
                                nowUtc)
                            .SetProperty(
                                x => x.LastError,
                                (string?)null)
                            .SetProperty(
                                x => x.NextAttemptAtUtc,
                                (DateTime?)null)
                            .SetProperty(
                                x => x.LockedBy,
                                (string?)null)
                            .SetProperty(
                                x => x.LockedUntilUtc,
                                (DateTime?)null),
                        cancellationToken);

            if (affectedRows != 1)
            {
                throw new OutboxOwnershipLostException(
                    messageId,
                    lockId,
                    $"Outbox message could not be marked as processed. " +
                    $"MessageId={messageId}, LockId={lockId}. " +
                    $"The lock may have been lost or the message may already be finalized.");
            }
        }

        // =====================================================
        // MARK AS FAILED
        // =====================================================

        public async Task MarkAsFailedAsync(
     Guid messageId,
     string lockId,
     string error,
     CancellationToken cancellationToken = default)
        {
            var message =
                await _dbContext.OutboxMessages
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.Id == messageId &&
                            x.ProcessedAtUtc == null &&
                            x.DeadLetteredAtUtc == null &&
                            x.LockedBy == lockId,
                        cancellationToken);

            if (message is null)
            {
                throw new InvalidOperationException(
                    $"Outbox message could not be marked as failed. " +
                    $"MessageId={messageId}, LockId={lockId}. " +
                    $"The lock may have been lost.");
            }

            var newRetryCount =
                message.RetryCount + 1;

            var delay =
                GetRetryDelay(newRetryCount);

            var nextAttemptAtUtc =
                DateTime.UtcNow.Add(delay);

            var affectedRows =
                await _dbContext.OutboxMessages
                    .Where(x =>
                        x.Id == messageId &&
                        x.ProcessedAtUtc == null &&
                        x.DeadLetteredAtUtc == null &&
                        x.LockedBy == lockId)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                x => x.RetryCount,
                                newRetryCount)
                            .SetProperty(
                                x => x.LastError,
                                error)
                            .SetProperty(
                                x => x.NextAttemptAtUtc,
                                nextAttemptAtUtc)
                            .SetProperty(
                                x => x.LockedBy,
                                (string?)null)
                            .SetProperty(
                                x => x.LockedUntilUtc,
                                (DateTime?)null),
                        cancellationToken);

            if (affectedRows != 1)
            {
                throw new OutboxOwnershipLostException(
                    messageId,
                    lockId,
                    $"Outbox failure state could not be persisted. " +
                    $"MessageId={messageId}, LockId={lockId}. " +
                    $"The lock may have been lost.");
            }
        }

        // =====================================================
        // MARK AS DEAD LETTER
        // =====================================================

        public async Task MarkAsDeadLetterAsync(
    Guid messageId,
    string lockId,
    string reason,
    CancellationToken cancellationToken = default)
        {
            var nowUtc =
                DateTime.UtcNow;

            var affectedRows =
                await _dbContext.OutboxMessages
                    .Where(x =>
                        x.Id == messageId &&
                        x.ProcessedAtUtc == null &&
                        x.DeadLetteredAtUtc == null &&
                        x.LockedBy == lockId)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                x => x.DeadLetteredAtUtc,
                                nowUtc)
                            .SetProperty(
                                x => x.DeadLetterReason,
                                reason)
                            .SetProperty(
                                x => x.NextAttemptAtUtc,
                                (DateTime?)null)
                            .SetProperty(
                                x => x.LockedBy,
                                (string?)null)
                            .SetProperty(
                                x => x.LockedUntilUtc,
                                (DateTime?)null),
                        cancellationToken);

            if (affectedRows != 1)
            {
                throw new OutboxOwnershipLostException(
                    messageId,
                    lockId,
                    $"Outbox message could not be dead-lettered. " +
                    $"MessageId={messageId}, LockId={lockId}. " +
                    $"The lock may have been lost or the message may already be finalized.");
            }
        }

        public async Task<bool> RenewLockAsync(
    Guid messageId,
    string lockId,
    DateTime newLockedUntilUtc,
    CancellationToken cancellationToken = default)
        {
            var affectedRows =
                await _dbContext.OutboxMessages
                    .Where(x =>
                        x.Id == messageId &&
                        x.ProcessedAtUtc == null &&
                        x.DeadLetteredAtUtc == null &&
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
        // COUNT PENDING
        // Process edilmemiş ve Dead Letter'a düşmemiş
        // tüm Outbox mesajlarını sayar.
        // =====================================================

        public async Task<int> CountPendingAsync(
            CancellationToken cancellationToken = default)
        {
            return await _dbContext.OutboxMessages
                .AsNoTracking()
                .CountAsync(
                    x =>
                        x.ProcessedAtUtc == null &&
                        x.DeadLetteredAtUtc == null,
                    cancellationToken);
        }

        // =====================================================
        // RETRY BACKOFF
        // =====================================================

        private static TimeSpan GetRetryDelay(
    int retryCount)
        {
            var baseDelay = retryCount switch
            {
                <= 1 => TimeSpan.FromSeconds(2),
                2 => TimeSpan.FromSeconds(5),
                3 => TimeSpan.FromSeconds(15),
                4 => TimeSpan.FromSeconds(30),
                5 => TimeSpan.FromMinutes(1),
                6 => TimeSpan.FromMinutes(2),
                7 => TimeSpan.FromMinutes(5),
                _ => TimeSpan.FromMinutes(10)
            };

            // Küçük jitter:
            // Birden fazla mesaj aynı anda retry olduğunda hepsinin
            // aynı milisaniyede broker'a yüklenmesini azaltır.
            var jitterSeconds =
                Random.Shared.Next(0, 6);

            return baseDelay.Add(
                TimeSpan.FromSeconds(jitterSeconds));
        }
    }
}