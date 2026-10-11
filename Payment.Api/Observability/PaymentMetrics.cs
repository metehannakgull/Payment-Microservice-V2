using Payment.Application.Abstractions.Observability;
using System.Diagnostics.Metrics;

namespace Payment.Api.Observability
{
    public sealed class PaymentMetrics :
        IPaymentMetrics
    {
        public const string MeterName =
            "PaymentSystem";

        private readonly Counter<long> _paymentsStarted;
        private readonly Counter<long> _paymentsSucceeded;
        private readonly Counter<long> _paymentsFailed;

        private readonly Histogram<double> _paymentAmount;
        private readonly Histogram<double> _paymentDuration;

        private readonly Counter<long> _outboxPublishSucceeded;
        private readonly Counter<long> _outboxPublishFailed;
        private readonly Counter<long> _outboxPublishTimedOut;
        private readonly Counter<long> _outboxOwnershipLost;
        private long _outboxPending;

        public PaymentMetrics(
            IMeterFactory meterFactory)
        {
            var meter =
                meterFactory.Create(
                    MeterName);

            _paymentsStarted =
                meter.CreateCounter<long>(
                    "payment.started");

            _paymentsSucceeded =
                meter.CreateCounter<long>(
                    "payment.succeeded");

            _paymentsFailed =
                meter.CreateCounter<long>(
                    "payment.failed");

            _paymentAmount =
                meter.CreateHistogram<double>(
                    "payment.amount",
                    unit: "currency");

            _paymentDuration =
                meter.CreateHistogram<double>(
                    "payment.duration",
                    unit: "s",
                    description: "Payment processing duration in seconds.");

            _outboxPublishSucceeded =
                meter.CreateCounter<long>(
                    "outbox.publish.succeeded");

            _outboxPublishFailed =
                meter.CreateCounter<long>(
                    "outbox.publish.failed");

            _outboxPublishTimedOut =
                meter.CreateCounter<long>(
                    "outbox.publish.timeout");

            _outboxOwnershipLost =
                meter.CreateCounter<long>(
                    "outbox.ownership.lost");
            meter.CreateObservableGauge(
                "outbox.pending.messages",
                () => Interlocked.Read(ref _outboxPending),
                unit: "{message}",
                description: "Current number of pending outbox messages.");
        }

        public void PaymentStarted(
            string provider,
            string currency)
        {
            _paymentsStarted.Add(
                1,
                new KeyValuePair<string, object?>(
                    "provider",
                    provider),
                new KeyValuePair<string, object?>(
                    "currency",
                    currency));
        }

        public void PaymentSucceeded(
            string provider,
            string currency,
            decimal amount)
        {
            _paymentsSucceeded.Add(
                1,
                new KeyValuePair<string, object?>(
                    "provider",
                    provider),
                new KeyValuePair<string, object?>(
                    "currency",
                    currency));

            _paymentAmount.Record(
                (double)amount,
                new KeyValuePair<string, object?>(
                    "provider",
                    provider),
                new KeyValuePair<string, object?>(
                    "currency",
                    currency));
        }

        public void PaymentFailed(
            string provider,
            string currency)
        {
            _paymentsFailed.Add(
                1,
                new KeyValuePair<string, object?>(
                    "provider",
                    provider),
                new KeyValuePair<string, object?>(
                    "currency",
                    currency));
        }

        public void PaymentDuration(
    string provider,
    string currency,
    string status,
    double durationSeconds)
        {
            _paymentDuration.Record(
                durationSeconds,
                new KeyValuePair<string, object?>(
                    "provider",
                    provider),
                new KeyValuePair<string, object?>(
                    "currency",
                    currency),
                new KeyValuePair<string, object?>(
                    "status",
                    status));
        }
        public void OutboxPublishSucceeded(
            string eventType)
        {
            _outboxPublishSucceeded.Add(
                1,
                new KeyValuePair<string, object?>(
                    "event_type",
                    eventType));
        }

        public void OutboxPublishFailed(
            string eventType,
            string failureKind)
        {
            _outboxPublishFailed.Add(
                1,
                new KeyValuePair<string, object?>(
                    "event_type",
                    eventType),
                new KeyValuePair<string, object?>(
                    "failure_kind",
                    failureKind));
        }

        public void OutboxPublishTimedOut(
            string eventType)
        {
            _outboxPublishTimedOut.Add(
                1,
                new KeyValuePair<string, object?>(
                    "event_type",
                    eventType));
        }

        public void OutboxOwnershipLost(
            string eventType)
        {
            _outboxOwnershipLost.Add(
                1,
                new KeyValuePair<string, object?>(
                    "event_type",
                    eventType));
        }
        public void SetOutboxPending(
    int count)
        {
            Interlocked.Exchange(
                ref _outboxPending,
                count);
        }
    }
}
