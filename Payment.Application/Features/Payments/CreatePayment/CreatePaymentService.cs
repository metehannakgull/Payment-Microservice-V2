using Payment.Messaging.Abstractions.Publishing;
using Payment.Application.Abstractions.Payments;
using Payment.Application.Abstractions.Persistence;
using Payment.Contracts.Events.Payments;
using Payment.Application.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PaymentEntity = Payment.Domain.Entities.Payment;
using Payment.Application.Abstractions.Idempotency;
using System.Globalization;
using System.Security.Cryptography;
using Payment.Application.Abstractions.Observability;
using Microsoft.Extensions.Logging;
using Payment.Application.Abstractions.Outbox;
using System.Text.Json;
using System.Diagnostics;

namespace Payment.Application.Features.Payments.CreatePayment
{
    public class CreatePaymentService
    {
        private readonly IPaymentRepository _paymentRepository;
        private readonly IPaymentProviderFactory _paymentProviderFactory;
        private readonly IOutboxStore _outboxStore;
        private readonly IPaymentIdempotencyStore _idempotencyStore;
        private readonly ICorrelationContext _correlationContext;
        private readonly ILogger<CreatePaymentService> _logger;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPaymentMetrics _paymentMetrics;
        public CreatePaymentService(
            IPaymentRepository paymentRepository,
            IPaymentProviderFactory paymentProviderFactory,
            IOutboxStore outboxStore,
            IPaymentIdempotencyStore idempotencyStore,
            ICorrelationContext correlationContext,
            ILogger<CreatePaymentService> logger,
            IUnitOfWork unitOfWork,
            IPaymentMetrics paymentMetrics)
        {
            _paymentRepository = paymentRepository;
            _paymentProviderFactory = paymentProviderFactory;
            _outboxStore = outboxStore;
            _idempotencyStore = idempotencyStore;
            _correlationContext = correlationContext;
            _logger = logger;
            _unitOfWork = unitOfWork;
            _paymentMetrics = paymentMetrics;
        }

        public async Task<CreatePaymentResult> ExecuteAsync(
            CreatePaymentCommand command,
            CancellationToken cancellationToken = default)
        {
            // =====================================================
            // 1. IDEMPOTENCY KEY VALIDATION
            // =====================================================

            if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                throw new ArgumentException(
                    "Idempotency-Key is required.");
            }

            var idempotencyKey =
                command.IdempotencyKey.Trim();

            // =====================================================
            // 2. REQUEST HASH
            //
            // Aynı Idempotency-Key farklı ödeme bilgileriyle
            // kullanılıyor mu anlayabilmek için.
            // =====================================================

            var requestHash =
                GenerateRequestHash(command);

            // =====================================================
            // 3. DAHA ÖNCE BU KEY KULLANILDI MI?
            // =====================================================

            var existingRecord =
                await _idempotencyStore.GetAsync(
                    idempotencyKey,
                    cancellationToken);

            if (existingRecord is not null)
            {
                return HandleExistingRecord(
                    existingRecord,
                    requestHash);
            }

            // =====================================================
            // 4. ATOMIC RESERVATION
            //
            // Aynı anda iki request gelirse sadece biri
            // bu key'i reserve edebilmeli.
            // =====================================================

            var reserved =
                await _idempotencyStore.TryReserveAsync(
                    idempotencyKey,
                    requestHash,
                    cancellationToken);

            if (!reserved)
            {
                // Biz GetAsync yaptıktan hemen sonra başka request
                // key'i reserve etmiş olabilir.
                //
                // Bu bir race-condition senaryosudur.

                existingRecord =
                    await _idempotencyStore.GetAsync(
                        idempotencyKey,
                        cancellationToken);

                if (existingRecord is not null)
                {
                    return HandleExistingRecord(
                        existingRecord,
                        requestHash);
                }

                throw new IdempotencyInProgressException(
                    "Payment request is already being processed.");
            }

            // =====================================================
            // 5. PAYMENT OLUŞTUR
            // =====================================================
            var paymentStopwatch = Stopwatch.StartNew();

            var payment = new PaymentEntity(
                command.OrderId,
                command.Amount,
                command.Currency,
                command.Provider);

            using var logScope = _logger.BeginScope(
                new Dictionary<string, object>
                {
                    ["CorrelationId"] = _correlationContext.CorrelationId,
                    ["PaymentId"] = payment.Id,
                    ["OrderId"] = payment.OrderId,
                    ["Provider"] = payment.Provider.ToString()
                });

            await _paymentRepository.AddAsync(
                payment,
                cancellationToken);

            payment.MarkAsProcessing();

            await _paymentRepository.UpdateAsync(
                payment,
                cancellationToken);

            _logger.LogInformation(
                "Payment processing started. Amount: {Amount} {Currency}",
                payment.Amount,
                payment.Currency);
            _paymentMetrics.PaymentStarted(
    payment.Provider.ToString(),
    payment.Currency.ToString());
            // =====================================================
            // 6. PAYMENT PROVIDER
            // =====================================================

            var provider = _paymentProviderFactory
                .GetProvider(command.Provider);

            var providerResult =
                await provider.PayAsync(
                    new PaymentRequest
                    {
                        OrderId = command.OrderId,
                        Amount = command.Amount,
                        Currency = command.Currency,
                        Provider = command.Provider
                    },
                    cancellationToken);

            OutboxMessage outboxMessage;

            // =====================================================
            // 7. SUCCESS / FAILED EVENT
            // =====================================================


            if (providerResult.IsSuccess)
            {
                payment.MarkAsSuccessful(providerResult.TransactionId);

                _logger.LogInformation(
                    "Payment completed successfully. TransactionId: {TransactionId}",
                    providerResult.TransactionId);

                var completedEvent =
                    new PaymentCompletedEvent(
                        payment.Id,
                        payment.OrderId,
                        payment.Amount,
                        payment.Currency.ToString(),
                        payment.Provider.ToString(),
                        providerResult.TransactionId,
                        payment.CompletedAt ?? DateTime.UtcNow)
                    {
                        CorrelationId =
                            _correlationContext.CorrelationId
                    };

                outboxMessage = new OutboxMessage
                {
                    Id = completedEvent.EventId,
                    EventType = nameof(PaymentCompletedEvent),
                    Payload = JsonSerializer.Serialize(completedEvent),
                    OccurredAtUtc = completedEvent.OccurredAtUtc
                };
            }
            else
            {
                payment.MarkAsFailed(
                    providerResult.ErrorCode,
                    providerResult.ErrorMessage);

                _logger.LogWarning(
                    "Payment failed. ErrorCode: {ErrorCode}, ErrorMessage: {ErrorMessage}",
                    providerResult.ErrorCode,
                    providerResult.ErrorMessage);

                var failedEvent =
                    new PaymentFailedEvent(
                        payment.Id,
                        payment.OrderId,
                        payment.Amount,
                        payment.Currency.ToString(),
                        payment.Provider.ToString(),
                        providerResult.ErrorCode,
                        providerResult.ErrorMessage,
                        DateTime.UtcNow)
                    {
                        CorrelationId =
                            _correlationContext.CorrelationId
                    };

                outboxMessage = new OutboxMessage
                {
                    Id = failedEvent.EventId,
                    EventType = nameof(PaymentFailedEvent),
                    Payload = JsonSerializer.Serialize(failedEvent),
                    OccurredAtUtc = failedEvent.OccurredAtUtc
                };
            }

            // =====================================================
            // 8. RESPONSE OLUŞTUR
            // =====================================================

            var result = new CreatePaymentResult
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                Status = payment.Status,
                TransactionId = providerResult.TransactionId,
                ErrorCode = providerResult.ErrorCode,
                ErrorMessage = providerResult.ErrorMessage
            };

            // =====================================================
            // 9. IDEMPOTENCY RECORD'I COMPLETED YAP
            // =====================================================

            var completedRecord =
                new PaymentIdempotencyRecord
                {
                    Key = idempotencyKey,
                    RequestHash = requestHash,
                    Status = IdempotencyStatus.Completed,
                    Result = result,
                    CreatedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow
                };

            await _unitOfWork.ExecuteInTransactionAsync(
     async ct =>
     {
         // Payment'in SUCCESS / FAILED nihai durumunu kaydet.
         await _paymentRepository.UpdateAsync(
             payment,
             ct);

         // Integration event'i kalıcı Outbox'a yaz.
         await _outboxStore.AddAsync(
             outboxMessage,
             ct);

         // Aynı Idempotency-Key için sonucu Completed yap.
         await _idempotencyStore.CompleteAsync(
             idempotencyKey,
             completedRecord,
             ct);
     },
     cancellationToken);


            _logger.LogInformation(
    "Payment transaction committed. PaymentId: {PaymentId}, EventId: {EventId}",
    payment.Id,
    outboxMessage.Id);

            paymentStopwatch.Stop();

            if (providerResult.IsSuccess)
            {
                _paymentMetrics.PaymentSucceeded(
                    payment.Provider.ToString(),
                    payment.Currency.ToString(),
                    payment.Amount);

                _paymentMetrics.PaymentDuration(
                    payment.Provider.ToString(),
                    payment.Currency.ToString(),
                    "succeeded",
                    paymentStopwatch.Elapsed.TotalSeconds);
            }
            else
            {
                _paymentMetrics.PaymentFailed(
                    payment.Provider.ToString(),
                    payment.Currency.ToString());

                _paymentMetrics.PaymentDuration(
                    payment.Provider.ToString(),
                    payment.Currency.ToString(),
                    "failed",
                    paymentStopwatch.Elapsed.TotalSeconds);
            }

            return result;
        }

        // =========================================================
        // EXISTING IDEMPOTENCY RECORD
        // =========================================================

        private static CreatePaymentResult HandleExistingRecord(
            PaymentIdempotencyRecord record,
            string currentRequestHash)
        {
            // Aynı key ama request farklı.
            if (!string.Equals(
                record.RequestHash,
                currentRequestHash,
                StringComparison.Ordinal))
            {
                throw new IdempotencyConflictException(
                    "The same Idempotency-Key cannot be used with different payment data.");
            }

            // Önceki işlem tamamlanmışsa eski sonucu döndür.
            if (record.Status == IdempotencyStatus.Completed &&
                record.Result is not null)
            {
                return record.Result;
            }

            // Aynı key halen başka request tarafından işleniyor.
            throw new IdempotencyInProgressException(
                "A payment request with this Idempotency-Key is already being processed.");
        }

        // =========================================================
        // REQUEST HASH
        // =========================================================

        private static string GenerateRequestHash(
            CreatePaymentCommand command)
        {
            var normalizedAmount =
                command.Amount.ToString(
                    "0.############################",
                    CultureInfo.InvariantCulture);

            var canonicalRequest =
                $"{command.OrderId.Trim()}|" +
                $"{normalizedAmount}|" +
                $"{(int)command.Currency}|" +
                $"{(int)command.Provider}";

            var bytes =
                Encoding.UTF8.GetBytes(
                    canonicalRequest);

            var hash =
                SHA256.HashData(bytes);

            return Convert.ToHexString(hash);
        }
    }
}
