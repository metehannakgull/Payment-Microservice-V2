# Payment System — Technical Architecture, Reliability Patterns & Engineering Notes

> **Project type:** .NET 8 ASP.NET Core Web API  
> **Architecture:** Clean Architecture + Hexagonal / Ports & Adapters + Event-Driven Microservices  
> **Messaging:** RabbitMQ  
> **Persistence:** SQL Server + EF Core  
> **Reliability:** Transactional Outbox, Idempotency, Consumer Inbox, Retry/Backoff/Jitter, Lease/Claim, Publisher Confirms  
> **Observability:** Structured Logging, Correlation ID, Health Checks, OpenTelemetry Metrics, Prometheus

---

## 1. Purpose of This Document

This document is a technical companion to the main README. It explains the architectural decisions, reliability patterns, messaging semantics, failure handling, test strategy, and observability model implemented in this payment system.

The project started as a virtual POS / payment API and evolved into a more production-oriented distributed system with explicit focus on:

- reliability and fault tolerance,
- message delivery guarantees,
- API and consumer idempotency,
- transactional consistency,
- retry safety,
- RabbitMQ recovery,
- Outbox processing,
- graceful shutdown,
- health monitoring,
- metrics and observability,
- failure-oriented testing.

The architecture is intentionally designed so that infrastructure technologies can evolve without forcing the business layer to change.

---

# 2. High-Level Solution Structure

```text
Solution Payment.Api
│
├── BuildingBlocks
│   ├── Payment.Contracts
│   └── Payment.Messaging.Abstractions
│
├── Services
│   ├── Payment
│   │   ├── Payment.Api
│   │   ├── Payment.Application
│   │   ├── Payment.Domain
│   │   ├── Payment.Infrastructure
│   │   └── Payment.Persistence
│   │
│   └── Notification
│       └── Payment.NotificationWorker
│
└── Tests
```

This structure reflects several architectural ideas at the same time:

- Clean Architecture
- Hexagonal Architecture / Ports & Adapters
- Dependency Inversion
- Microservice-oriented separation
- Event-driven communication

The core business logic does not directly depend on RabbitMQ, SQL Server, EF Core, or ASP.NET Core.

---

# 3. Architectural Patterns

## 3.1 Clean Architecture

Responsibilities are separated into layers with clear dependency direction.

### Domain

Contains core business concepts such as:

- Payment entity
- Payment status
- Payment provider
- domain rules
- domain exceptions
- value objects / enums

The Domain layer has no infrastructure dependency.

### Application

Contains use cases and ports such as:

- `CreatePaymentService`
- `IPaymentRepository`
- `IPaymentProvider`
- `IPaymentProviderFactory`
- `IIntegrationEventPublisher`
- `IOutboxStore`
- `IUnitOfWork`
- `ICorrelationContext`
- `IPaymentMetrics`

The Application layer defines **what the system needs**. Infrastructure layers define **how those needs are implemented**.

---

## 3.2 Dependency Inversion

The business layer depends on interfaces instead of concrete infrastructure.

```text
CreatePaymentService
        |
        v
IPaymentRepository
        |
        +---------------------+
        |                     |
        v                     v
EfCorePaymentRepository   InMemoryPaymentRepository
```

Messaging follows the same idea:

```text
Application
   |
   v
IIntegrationEventPublisher
   |
   v
RabbitMqIntegrationEventPublisher
```

---

## 3.3 Repository Pattern

Persistence is abstracted behind repository/store interfaces.

Examples:

- `IPaymentRepository`
- `IOutboxStore`

This keeps business logic independent from EF Core details.

---

## 3.4 Strategy Pattern

Different payment providers implement the same contract:

```csharp
IPaymentProvider
```

This allows provider-specific payment behavior without changing `CreatePaymentService`.

Future adapters can include bank virtual POS integrations or third-party gateways.

---

## 3.5 Factory Pattern

Provider resolution is handled through:

```csharp
IPaymentProviderFactory
```

The application requests the required provider without constructing concrete classes itself.

---

## 3.6 Adapter Pattern

RabbitMQ, SQL Server, EF Core, payment providers, and HTTP-specific correlation handling are adapters around application-defined ports.

---

# 4. Payment Processing Flow

```text
Client
  |
  v
PaymentsController
  |
  v
CreatePaymentService
  |
  +--> Validate request
  |
  +--> Check Idempotency-Key
  |
  +--> Create Payment entity
  |
  +--> Resolve provider
  |
  +--> Execute provider payment
  |
  +--> Update Payment state
  |
  +--> Create integration event
  |
  +--> Persist Payment + Outbox + Idempotency atomically
  |
  v
Return API response
```

RabbitMQ publishing is intentionally **not** performed inside the request transaction. The event is first persisted to the Outbox table and published asynchronously by a background worker.

---

# 5. API Idempotency

Payment APIs must defend against duplicate requests caused by:

- double-clicks,
- client retries,
- mobile reconnects,
- reverse-proxy retries,
- timeout retries,
- repeated submissions.

The API supports an `Idempotency-Key` header.

Example:

```http
Idempotency-Key: payment-2026-001
```

The system stores a persistent idempotency record and computes a SHA-256 hash of the canonical request.

Behavior:

```text
Same key + same body
        |
        v
Return cached result
```

```text
Same key + different body
        |
        v
409 Conflict
```

The final idempotency result is persisted in the same transactional boundary as the payment and Outbox event.

---

# 6. Transactional Outbox Pattern

## 6.1 The Dual-Write Problem

Without an Outbox, this sequence is unsafe:

```text
1. Save payment to SQL Server
2. Publish event to RabbitMQ
```

Possible inconsistency:

```text
SQL commit = success
RabbitMQ publish = failure
```

The payment exists, but downstream services never receive the event.

The reverse order is also unsafe:

```text
RabbitMQ publish = success
SQL transaction = failure
```

Then consumers receive an event for a payment that was never durably committed.

---

## 6.2 Durable Solution

The system persists the business state and integration event atomically:

```text
SQL Transaction
|
+-- Payment
+-- OutboxMessage
+-- Idempotency Result
|
COMMIT
```

RabbitMQ publication happens later.

The database becomes the durable handoff point between payment processing and messaging.

---

# 7. Outbox Message Model

The Outbox model contains fields such as:

```text
Id
EventType
Payload
OccurredAtUtc
ProcessedAtUtc
RetryCount
LastError
NextAttemptAtUtc
DeadLetteredAtUtc
DeadLetterReason
LockedBy
LockedUntilUtc
```

These fields enable:

- durable event storage,
- retry scheduling,
- failure diagnostics,
- dead-letter handling,
- distributed worker ownership,
- lease renewal,
- crash recovery.

---

# 8. Atomic Outbox Claiming

Multiple API instances may run simultaneously, so two workers must not process the same row at the same time.

The SQL Server implementation uses:

```sql
UPDLOCK
READPAST
ROWLOCK
```

with a CTE-based `UPDATE ... OUTPUT` claim query.

Conceptually:

```text
Worker A                 Worker B
   |                        |
   | claim row #1           |
   |----------------------->|
   |                        |
   | row locked             |
   |                        | tries to claim
   |                        | skips locked row
```

This eliminates the race window created by a naive:

```text
SELECT
then UPDATE
```

approach.

---

# 9. Lease / Ownership Model

When a worker claims an Outbox message, it writes:

```text
LockedBy
LockedUntilUtc
```

Example:

```text
LockedBy       = worker-instance-id
LockedUntilUtc = now + 60 seconds
```

The worker owns the message only while the lease is valid.

## 9.1 Heartbeat

Current timing model:

```text
Lease duration     = 60 seconds
Heartbeat interval = 20 seconds
Publish timeout    = 30 seconds
```

The heartbeat calls:

```csharp
RenewLockAsync(...)
```

If lease renewal fails, the worker treats ownership as lost and cancels the active publish attempt.

## 9.2 Ownership-Safe Finalization

Finalization methods update rows only if the current worker still owns them.

For example, `MarkAsProcessedAsync` requires:

```text
Id matches
ProcessedAtUtc is null
DeadLetteredAtUtc is null
LockedBy == current worker
```

If no row is updated, an `OutboxOwnershipLostException` is raised.

This prevents a stale worker from finalizing a row after another worker has taken ownership.

---

# 10. Retry, Backoff and Jitter

Transient failures are retried.

Current retry schedule:

```text
Retry 1  -> 2 seconds
Retry 2  -> 5 seconds
Retry 3  -> 15 seconds
Retry 4  -> 30 seconds
Retry 5  -> 1 minute
Retry 6  -> 2 minutes
Retry 7  -> 5 minutes
Retry 8+ -> 10 minutes
```

A jitter of roughly `0-5 seconds` is added to reduce synchronized retry storms.

Retry state uses:

```text
RetryCount
LastError
NextAttemptAtUtc
```

---

# 11. Failure Classification

Publish failures are classified into categories.

Transient examples:

- broker unavailable,
- connection refused,
- network interruption,
- closed connection,
- I/O failure.

Permanent/non-recoverable examples can include:

- malformed payload,
- unsupported event type,
- permanent publish failure.

Temporary infrastructure failures are therefore retried rather than immediately dead-lettered.

---

# 12. RabbitMQ Publisher Confirms

Publishing does not end at `BasicPublishAsync`.

The producer waits for broker confirmation.

```text
Application
    |
    | publish
    v
RabbitMQ
    |
    | publisher confirm
    v
Application
```

A publisher confirm means the broker accepted the message.

It does **not** mean a downstream consumer processed it.

---

# 13. Mandatory Publishing and Topology

The producer uses RabbitMQ topic-based event publishing and mandatory routing behavior.

Current exchange:

```text
payment.events
```

Example routing concept:

```text
payment.completed.*
```

Mandatory publishing helps surface unroutable message scenarios.

---

# 14. RabbitMQ Connection Management

The RabbitMQ connection is reused instead of opening a new TCP connection per event.

The connection component includes:

- singleton connection reuse,
- `SemaphoreSlim` synchronization,
- automatic recovery,
- topology recovery,
- lifecycle logging.

Relevant recovery options:

```text
AutomaticRecoveryEnabled = true
TopologyRecoveryEnabled  = true
NetworkRecoveryInterval  = 5 seconds
```

---

# 15. Outbox Publisher Worker

The API hosts a background service that processes Outbox rows.

```text
loop
 |
 +--> atomic claim
 |
 +--> establish lease
 |
 +--> start heartbeat
 |
 +--> publish event
 |
 +--> wait for broker confirm
 |
 +--> stop heartbeat
 |
 +--> mark Outbox processed
 |
 +--> sleep
```

The worker is responsible for retrying failures without coupling RabbitMQ availability to the original HTTP request.

---

# 16. Publish Timeout

RabbitMQ publish operations are bounded by a timeout.

Current timeout:

```text
30 seconds
```

Timeout flow:

```text
publish does not complete
        |
        v
publish timeout cancellation
        |
        v
heartbeat stopped
        |
        v
message marked failed
        |
        v
retry scheduled
```

This scenario was explicitly tested using a controlled artificial delay.

After removing the delay, the same durable Outbox message was retried successfully.

---

# 17. Graceful Shutdown

Shutdown is treated differently from operational failure.

During controlled host shutdown:

```text
host cancellation
        |
        v
active publish cancelled
        |
        v
worker exits safely
```

A deployment or Ctrl+C should not incorrectly increment retry counters or persist a fake broker failure.

The message remains recoverable after lease expiration and can be reclaimed by a future worker instance.

---

# 18. Notification Consumer

A separate service exists:

```text
Payment.NotificationWorker
```

Its responsibilities include:

- RabbitMQ consumption,
- manual ACK,
- retry handling,
- DLQ handling,
- prefetch control,
- consumer idempotency,
- graceful shutdown.

---

# 19. Consumer-Owned Queues

Each consuming service should own its own queue.

```text
PaymentCompletedEvent
        |
        v
payment.events exchange
        |
        +--> notification queue
        |
        +--> future audit queue
        |
        +--> future reporting queue
        |
        +--> future accounting queue
```

This preserves service autonomy and allows multiple consumers to react independently to the same event.

---

# 20. Consumer Inbox / Idempotent Consumer

RabbitMQ is treated as an at-least-once delivery system, so duplicate delivery is possible.

The Notification Worker uses a persistent Inbox table:

```text
ProcessedMessages
```

Important fields include:

```text
EventId
ConsumerName
ProcessedAtUtc
Status
ReservedAtUtc
LockedBy
LockedUntilUtc
LastError
```

The consumer checks/reserves an event before applying business side effects.

Possible reservation outcomes include:

```text
Acquired
AlreadyProcessed
InProgress
```

### AlreadyProcessed

The message is ACKed safely because the business effect was already completed.

### InProgress

The message is delayed through the retry path instead of being processed concurrently.

---

# 21. Consumer Retry Queue and TTL

The Notification Worker uses a delayed retry queue.

Current retry delay:

```text
5 seconds
```

After TTL expiration, RabbitMQ routes the message back for another attempt.

This avoids tight retry loops.

---

# 22. Dead Letter Queue

Messages that cannot be handled according to the configured retry policy can be moved to a DLQ.

DLQ supports:

- manual inspection,
- alerting,
- replay,
- debugging,
- forensic analysis.

---

# 23. Manual ACK

Consumer acknowledgements are manual.

A message is ACKed only after:

```text
handler completes
+
Inbox state is persisted as processed
```

This avoids losing messages when the consumer crashes during handling.

---

# 24. At-Least-Once Delivery Model

The system intentionally targets:

```text
At-least-once delivery
```

instead of claiming generic exactly-once delivery across SQL Server and RabbitMQ.

Reliable business effects are achieved through:

```text
API Idempotency
+
Transactional Outbox
+
Publisher Confirms
+
Manual ACK
+
Consumer Inbox
+
Idempotent Handlers
```

---

# 25. Correlation ID

The API supports:

```http
X-Correlation-ID
```

If not provided, the application generates one.

Correlation ID is propagated into integration events so that operations can be connected across:

```text
HTTP request
  |
  v
Payment processing
  |
  v
Outbox
  |
  v
RabbitMQ
  |
  v
Notification Worker
```

---

# 26. Structured Logging

The system uses `ILogger` structured logging.

Example:

```csharp
_logger.LogInformation(
    "Payment transaction committed. PaymentId: {PaymentId}, EventId: {EventId}",
    payment.Id,
    eventId);
```

Structured properties can be indexed and filtered by centralized logging systems.

---

# 27. Health Checks

The API exposes:

```text
/health/live
/health/ready
```

## Liveness

Answers:

> Is the process itself alive?

## Readiness

Answers:

> Can this instance currently perform its required work?

Readiness checks include SQL Server and RabbitMQ.

Expected behavior tested:

```text
RabbitMQ down
/health/live  -> 200
/health/ready -> 503
```

After broker recovery:

```text
/health/ready -> 200
```

---

# 28. OpenTelemetry and Prometheus

The project uses:

```text
System.Diagnostics.Metrics
OpenTelemetry
Prometheus exporter
```

Metrics are exposed from:

```text
/metrics
```

Service metadata is configured as:

```text
service_name="Payment.Api"
```

---

# 29. Payment Metrics

Custom metrics currently include:

```text
payment_started_total
payment_succeeded_total
payment_failed_total
payment_amount_currency
payment_duration_seconds
```

## payment_started_total

Counts payment processing attempts.

Tags:

```text
provider
currency
```

## payment_succeeded_total

Incremented only after the final payment transaction commits successfully.

## payment_failed_total

Represents provider-declared/business payment failures.

Technical failures are conceptually separated from business decline/failure metrics.

## payment_amount_currency

Histogram of successful payment amounts.

## payment_duration_seconds

Measures end-to-end payment processing latency from the application use case until final transaction commit.

This includes roughly:

```text
Payment creation
Provider call
Payment state transition
Outbox creation
Database persistence
Transaction commit
```

A successful Mock payment test produced a duration of approximately:

```text
0.836 seconds
```

---

# 30. Outbox Metrics

Custom Outbox metrics include:

```text
outbox_publish_succeeded_total
outbox_publish_failed_total
outbox_publish_timeout_total
outbox_ownership_lost_total
outbox_pending_messages
```

## outbox_publish_succeeded_total

Incremented after RabbitMQ publishing succeeds and the Outbox row is successfully finalized as processed.

## outbox_publish_failed_total

Counts failed publish **attempts**, not failed payments.

Useful `failure_kind` values include:

```text
transient
permanent
bad_payload
unsupported_event
unexpected
```

A single Outbox message can increment this counter multiple times because of retries.

## outbox_publish_timeout_total

Counts publish attempts cancelled by the configured publish timeout.

## outbox_ownership_lost_total

Counts cases where a worker loses ownership of a claimed Outbox message.

## outbox_pending_messages

Observable gauge representing current unfinished Outbox backlog.

Pending is defined as:

```text
ProcessedAtUtc IS NULL
AND
DeadLetteredAtUtc IS NULL
```

A message is still considered pending even if it:

- is currently locked,
- is waiting for `NextAttemptAtUtc`,
- has already failed one or more times.

---

# 31. Metric Cardinality Strategy

High-cardinality values are intentionally excluded from metric tags.

Good tags:

```text
provider
currency
status
event_type
failure_kind
```

Avoided metric tags:

```text
PaymentId
OrderId
EventId
CorrelationId
TransactionId
```

Those identifiers belong in logs and traces.

---

# 32. Failure-Oriented Tests Performed

The system has been intentionally tested under failure conditions.

## 32.1 RabbitMQ Down During Outbox Publish

Scenario:

```text
API running
RabbitMQ stopped
new payment created
```

Observed behavior:

```text
payment succeeds
Outbox remains durable
publish fails transiently
retry state increments
```

Observed metric:

```text
outbox_publish_failed_total{failure_kind="transient"}
```

After RabbitMQ restart:

```text
Outbox retries
publish succeeds
message finalized
```

---

## 32.2 Publish Timeout

A temporary delay longer than the 30-second timeout was injected.

Observed:

```text
publish cancellation
message marked failed
retry scheduled
```

Metric:

```text
outbox_publish_timeout_total
```

After removing the artificial delay, the durable message was retried successfully.

---

## 32.3 Ownership Loss

Lease renewal was intentionally forced to fail.

Observed:

```text
heartbeat detects loss of ownership
leaseLostCancellation is triggered
publish attempt is cancelled
unsafe DB finalization is avoided
```

Metric:

```text
outbox_ownership_lost_total
```

---

## 32.4 Graceful Shutdown During Active Publish

The API was stopped during active Outbox processing.

Verified behavior:

```text
shutdown cancellation
!= broker failure
```

The message was not incorrectly marked as a failed RabbitMQ operation and remained recoverable after the lease expired.

---

## 32.5 RabbitMQ Readiness Failure

With RabbitMQ unavailable:

```text
/health/live  = 200
/health/ready = 503
```

After restart:

```text
/health/ready = 200
```

---

## 32.6 Consumer Shutdown / Restart

The Notification Worker was tested during active delivery and restart scenarios to ensure controlled shutdown does not create unsafe duplicate handling.

---

## 32.7 Consumer InProgress Reservation

When an Inbox reservation indicates that another processing attempt is already active:

```text
reservation result = InProgress
```

the event is delayed/retried rather than processed twice concurrently.

---

# 33. SQL Server Persistence

Current persistence provider:

```text
SQL Server
```

Example local configuration:

```json
{
  "Persistence": {
    "Provider": "SqlServer"
  },
  "ConnectionStrings": {
    "PaymentDatabase": "Server=.\\SQLEXPRESS;Database=PaymentDb;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

The project currently uses manual SQL schema scripts rather than EF migrations, while EF Core remains the ORM.

---

# 34. Database Provider Flexibility

The design keeps room for future providers:

```text
SQL Server
PostgreSQL
MySQL
InMemory
```

However, atomic Outbox claim logic is provider-specific.

The current robust implementation is SQL Server-specific. Future PostgreSQL/MySQL adapters should use native locking semantics rather than reusing SQL Server hints.

---

# 35. InMemory Adapters

The project includes InMemory implementations for selected abstractions, including Outbox storage.

These are useful for:

- tests,
- local experimentation,
- infrastructure-independent development.

The InMemory and SQL-backed implementations follow the same application contracts.

---

# 36. RabbitMQ Local Development

Current local setup:

```text
Container: payment-rabbitmq
Image: rabbitmq:management
AMQP host port: 5673
Management UI: 15672
```

Useful commands:

```bash
docker start payment-rabbitmq
docker stop payment-rabbitmq
docker ps
docker ps -a
docker port payment-rabbitmq
```

Management UI:

```text
http://localhost:15672
```

---

# 37. Configuration and Secrets

Example local RabbitMQ configuration:

```json
{
  "Messaging": {
    "Provider": "RabbitMq",
    "RabbitMq": {
      "HostName": "127.0.0.1",
      "Port": 5673,
      "UserName": "guest",
      "VirtualHost": "/",
      "ExchangeName": "payment.events"
    }
  }
}
```

Production secrets should not be committed to GitHub. Real environments should use environment variables or a secret manager.

---

# 38. Consistency Model

The system combines:

```text
strong local transaction consistency
+
eventual consistency across services
```

Within the Payment database, the critical local state is coordinated transactionally.

Across RabbitMQ and downstream services, consistency is eventual.

---

# 39. Why RabbitMQ Is Not Inside the SQL Transaction

External network calls should not be blindly wrapped inside a long-running SQL transaction.

Doing so can create:

- long-held locks,
- deadlocks,
- reduced throughput,
- infrastructure coupling,
- difficult rollback semantics.

The final durable payment result and Outbox event are committed locally first. RabbitMQ delivery happens asynchronously afterward.

---

# 40. Observability Philosophy

The project separates:

```text
Logs
Metrics
Health
Correlation
```

### Logs answer

> What happened?

### Metrics answer

> How often, how many, and how slow?

### Health checks answer

> Can this service operate right now?

### Correlation ID answers

> Which distributed operations belong to the same request?

Tracing is a logical next step.

---

# 41. Known Engineering Considerations

## 41.1 Sequential Processing of a Claimed Batch

The worker currently claims a batch and then processes messages sequentially.

A subtle correctness/performance consideration exists when:

```text
batchSize = 20
```

is claimed upfront but heartbeat for later messages does not begin until each message reaches the processing loop.

Later leases can approach expiration while earlier messages are still being processed.

Possible refinements:

```text
batchSize = 1 for strict sequential semantics
```

or:

```text
bounded parallelism
+
per-message scope
+
per-message heartbeat
```

This should be revisited before high-throughput production use.

---

## 41.2 Outbox Pending Gauge

`outbox_pending_messages` uses a cached observable value that is refreshed by the Outbox Worker.

This avoids performing asynchronous database access from an ObservableGauge callback.

---

## 41.3 Prometheus Endpoint Exposure

`/metrics` is convenient in local development, but a production deployment should typically protect it through private networking, ingress rules, authentication, or internal scraping configuration.

---

## 41.4 OpenTelemetry Export Strategy

The project currently exposes Prometheus-compatible metrics directly.

For larger environments, an OpenTelemetry Collector and OTLP-based export may provide a more flexible architecture.

---

# 42. Recommended Next Engineering Steps

1. Complete and verify `outbox_pending_messages` gauge behavior.
2. Add payment technical-error metrics separate from business failures.
3. Add provider-specific latency analysis.
4. Add oldest-pending-Outbox-age gauge.
5. Add Outbox retry distribution metrics.
6. Add Notification Worker metrics.
7. Add OpenTelemetry tracing with `ActivitySource`.
8. Propagate trace context through RabbitMQ headers.
9. Add OpenTelemetry Collector.
10. Add Grafana dashboards.
11. Add Prometheus alert rules.
12. Add automated integration tests.
13. Add Testcontainers for RabbitMQ.
14. Add Testcontainers for SQL Server.
15. Automate broker-down, timeout, retry, shutdown, and recovery tests.
16. Revisit Outbox batch concurrency strategy.
17. Add provider-specific atomic claim adapters.
18. Add real virtual POS integrations.
19. Add production secrets management.

---

# 43. Suggested Production Alerts

Useful future alerts include:

```text
Outbox pending messages > threshold
Outbox oldest pending age > threshold
High transient publish failure rate
Any permanent Outbox failure
Ownership-loss spike
Payment failure rate above threshold
Payment p95 latency above threshold
RabbitMQ readiness failure
SQL Server readiness failure
Consumer DLQ growth
Inbox processing failures
```

---

# 44. Engineering Summary

The project now demonstrates and/or implements:

```text
.NET 8 Web API
Clean Architecture
Hexagonal Architecture
Dependency Inversion
Repository Pattern
Strategy Pattern
Factory Pattern
Adapter Pattern
Event-Driven Architecture
RabbitMQ
Topic Exchange
Publisher Confirms
Mandatory Publishing
Transactional Outbox
Atomic SQL Claiming
Lease-Based Ownership
Lease Heartbeat
Retry / Backoff / Jitter
Dead Letter Handling
At-Least-Once Delivery
Consumer Inbox
Idempotent Consumer
Manual ACK
Retry Queue with TTL
Graceful Shutdown
API Idempotency-Key
SHA-256 Request Hashing
Correlation ID
Structured Logging
SQL Server
EF Core
Health Checks
Liveness / Readiness
OpenTelemetry
Prometheus Metrics
Payment Latency Histogram
Outbox Failure Metrics
Failure-Oriented Testing
Recovery Testing
```

The most important architectural characteristic is that failures are treated as expected system behavior rather than as rare edge cases.

The system explicitly models:

```text
network failure
broker failure
timeout
duplicate request
duplicate event
consumer crash
worker shutdown
lease expiration
ownership loss
retry
recovery
```

---

# 45. End-to-End Reliability Chain

```text
Client Request
    |
    v
API Idempotency-Key
    |
    v
Payment Processing
    |
    v
SQL Transaction
    |
    +--> Payment
    +--> Idempotency Result
    +--> Outbox Event
    |
    v
COMMIT
    |
    v
Outbox Worker
    |
    +--> Atomic Claim
    +--> Lease
    +--> Heartbeat
    +--> Retry / Backoff
    |
    v
RabbitMQ Publish
    |
    v
Publisher Confirm
    |
    v
Mark Outbox Processed
    |
    v
Consumer Queue
    |
    v
Consumer Inbox Reservation
    |
    v
Handler
    |
    v
Inbox Marked Processed
    |
    v
Manual ACK
```

This architecture substantially reduces the risk of:

- duplicate payments,
- lost events,
- duplicate consumer side effects,
- unsafe retry behavior,
- inconsistent payment/message state,
- message loss during shutdown,
- concurrent worker conflicts,
- invisible infrastructure failures.

---

## Project Status

This project is under active development. The current focus is reliability engineering, Outbox correctness, failure recovery, and observability before real payment providers and production deployment concerns are added.
