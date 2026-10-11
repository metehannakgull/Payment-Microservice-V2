using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Application.Abstractions.Observability
{
    public interface IPaymentMetrics
    {
        void PaymentStarted(
            string provider,
            string currency);

        void PaymentSucceeded(
            string provider,
            string currency,
            decimal amount);

        void PaymentFailed(
            string provider,
            string currency);
        void PaymentDuration(
    string provider,
    string currency,
    string status,
    double durationSeconds);

        void OutboxPublishSucceeded(
            string eventType);

        void OutboxPublishFailed(
            string eventType,
            string failureKind);

        void OutboxPublishTimedOut(
            string eventType);

        void OutboxOwnershipLost(
            string eventType);
        void SetOutboxPending(int count);
    }
}
