using Payment.Domain.Enums.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace Payment.Domain.Entities
{
    public class Payment
    {
        public Guid Id { get; private set; }

        public string OrderId { get; private set; }

        public decimal Amount { get; private set; }

        public Currency Currency { get; private set; }

        public PaymentStatus Status { get; private set; }

        public PaymentProviderType Provider { get; private set; }

        public DateTime CreatedAt { get; private set; }

        public DateTime? CompletedAt { get; private set; }
        public string? TransactionId { get; private set; }

        public string? ErrorCode { get; private set; }

        public string? ErrorMessage { get; private set; }


        private Payment()
        {
        }


        public Payment(
            string orderId,
            decimal amount,
            Currency currency,
            PaymentProviderType provider)
        {
            if (string.IsNullOrWhiteSpace(orderId))
                throw new ArgumentException(
                    "OrderId cannot be empty.",
                    nameof(orderId));

            if (amount <= 0)
                throw new ArgumentException(
                    "Payment amount must be greater than zero.",
                    nameof(amount));

            Id = Guid.NewGuid();

            OrderId = orderId;

            Amount = amount;

            Currency = currency;

            Provider = provider;

            Status = PaymentStatus.Pending;

            CreatedAt = DateTime.UtcNow;
        }


        public void MarkAsProcessing()
        {
            Status = PaymentStatus.Processing;
        }


        public void MarkAsSuccessful(string? transactionId)
        {
            Status = PaymentStatus.Successful;

            TransactionId = transactionId;

            CompletedAt = DateTime.UtcNow;

            ErrorCode = null;

            ErrorMessage = null;
        }


        public void MarkAsFailed(
            string? errorCode,
            string? errorMessage)
        {
            Status = PaymentStatus.Failed;

            ErrorCode = errorCode;

            ErrorMessage = errorMessage;
        }


        public void MarkAsRequires3DSecure()
        {
            Status = PaymentStatus.Requires3DSecure;
        }


        public void Cancel()
        {
            Status = PaymentStatus.Cancelled;
        }
    }
}
