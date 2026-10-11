# Payment System — Teknik Mimari, Güvenilirlik Pattern'leri ve Mühendislik Notları

> **Proje türü:** .NET 8 ASP.NET Core Web API  
> **Mimari:** Clean Architecture + Hexagonal Architecture / Ports & Adapters + Event-Driven Microservices  
> **Mesajlaşma:** RabbitMQ  
> **Persistence:** SQL Server + EF Core  
> **Güvenilirlik:** Transactional Outbox, Idempotency, Consumer Inbox, Retry / Backoff / Jitter, Lease / Claim, Publisher Confirms  
> **Observability:** Structured Logging, Correlation ID, Health Checks, OpenTelemetry Metrics, Prometheus

---

## 1. Bu Dokümanın Amacı

Bu doküman ana README dosyasının teknik tamamlayıcısıdır. Payment sisteminde uygulanan mimari kararları, güvenilirlik pattern'lerini, mesajlaşma semantiklerini, hata yönetimini, test stratejisini ve observability yaklaşımını ayrıntılı şekilde açıklar.

Proje başlangıçta bir sanal POS / ödeme API'si olarak başladı; daha sonra özellikle aşağıdaki alanlara odaklanan, production'a daha yakın dağıtık sistem mimarisine dönüştü:

- güvenilirlik ve fault tolerance,
- mesaj teslim garantileri,
- API ve consumer idempotency,
- transactional consistency,
- güvenli retry davranışı,
- RabbitMQ recovery,
- Outbox processing,
- graceful shutdown,
- health monitoring,
- metrics ve observability,
- failure-oriented testing.

Mimari özellikle, altyapı teknolojileri değişse bile business katmanının minimum etkilenmesini sağlayacak şekilde tasarlanmıştır.

---

# 2. Üst Seviye Solution Yapısı

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

Bu yapı aynı anda birden fazla mimari yaklaşımı yansıtır:

- Clean Architecture
- Hexagonal Architecture / Ports & Adapters
- Dependency Inversion
- Microservice odaklı ayrıştırma
- Event-driven communication

Core business logic doğrudan RabbitMQ, SQL Server, EF Core veya ASP.NET Core'a bağımlı değildir.

---

# 3. Kullanılan Mimari Pattern'ler

## 3.1 Clean Architecture

Sorumluluklar, bağımlılık yönü net olacak şekilde katmanlara ayrılmıştır.

### Domain

Temel business kavramlarını içerir:

- Payment entity
- Payment status
- Payment provider
- domain kuralları
- domain exception'ları
- value object / enum yapıları

Domain katmanı infrastructure bağımlılığı taşımaz.

### Application

Use case'leri ve port'ları içerir:

- `CreatePaymentService`
- `IPaymentRepository`
- `IPaymentProvider`
- `IPaymentProviderFactory`
- `IIntegrationEventPublisher`
- `IOutboxStore`
- `IUnitOfWork`
- `ICorrelationContext`
- `IPaymentMetrics`

Application katmanı **sistemin neye ihtiyaç duyduğunu** tanımlar. Infrastructure katmanları ise **bu ihtiyacın nasıl karşılanacağını** uygular.

## 3.2 Dependency Inversion

Business katmanı concrete infrastructure implementasyonlarına değil interface'lere bağımlıdır.

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

Mesajlaşma tarafı da aynı prensibi izler:

```text
Application
   |
   v
IIntegrationEventPublisher
   |
   v
RabbitMqIntegrationEventPublisher
```

## 3.3 Repository Pattern

Persistence işlemleri repository/store interface'leri arkasına alınmıştır.

Örnekler:

- `IPaymentRepository`
- `IOutboxStore`

Bu yapı business logic'in EF Core detaylarına bağımlı olmasını engeller.

## 3.4 Strategy Pattern

Farklı payment provider'lar aynı contract'ı implement eder:

```csharp
IPaymentProvider
```

Böylece provider'a özel ödeme davranışları `CreatePaymentService` değiştirilmeden uygulanabilir.

## 3.5 Factory Pattern

Provider seçimi şu yapı üzerinden yapılır:

```csharp
IPaymentProviderFactory
```

Application katmanı ihtiyaç duyduğu provider'ı ister; concrete class'ı kendisi oluşturmaz.

## 3.6 Adapter Pattern

RabbitMQ, SQL Server, EF Core, payment provider'lar ve HTTP tabanlı correlation mekanizması application-defined port'ların adapter'ları olarak konumlanır.

---

# 4. Payment Processing Akışı

```text
Client
  |
  v
PaymentsController
  |
  v
CreatePaymentService
  |
  +--> Request validation
  |
  +--> Idempotency-Key kontrolü
  |
  +--> Payment entity oluşturma
  |
  +--> Provider çözümleme
  |
  +--> Provider payment çağrısı
  |
  +--> Payment state güncelleme
  |
  +--> Integration event oluşturma
  |
  +--> Payment + Outbox + Idempotency bilgisini atomik persist etme
  |
  v
API response
```

RabbitMQ publish işlemi bilinçli olarak request transaction'ının içinde yapılmaz. Event önce Outbox tablosuna yazılır ve daha sonra background worker tarafından asenkron olarak publish edilir.

---

# 5. API Idempotency

Payment API'lerinin aşağıdaki duplicate request senaryolarına karşı korunması gerekir:

- çift tıklama,
- client retry,
- mobil bağlantı kopup yeniden bağlanma,
- reverse proxy retry,
- timeout retry,
- tekrar gönderim.

API, `Idempotency-Key` header'ını destekler.

```http
Idempotency-Key: payment-2026-001
```

Sistem persistent idempotency kaydı tutar ve canonical request üzerinden SHA-256 hash üretir.

```text
Aynı key + aynı body
        |
        v
Cached result döndür
```

```text
Aynı key + farklı body
        |
        v
409 Conflict
```

Final idempotency sonucu Payment ve Outbox event ile aynı transactional boundary içinde persist edilir.

---

# 6. Transactional Outbox Pattern

## 6.1 Dual-Write Problemi

Outbox olmadan şu sequence güvenli değildir:

```text
1. Payment'ı SQL Server'a kaydet
2. Event'i RabbitMQ'ya publish et
```

Olası tutarsızlık:

```text
SQL commit = başarılı
RabbitMQ publish = başarısız
```

Payment veritabanında vardır fakat downstream servisler event'i hiç alamaz.

Tersi de güvensizdir:

```text
RabbitMQ publish = başarılı
SQL transaction = başarısız
```

Bu durumda consumer'lar kalıcı olarak commit edilmemiş bir payment için event alır.

## 6.2 Kalıcı ve Güvenli Çözüm

Business state ve integration event aynı SQL transaction içinde persist edilir:

```text
SQL Transaction
|
+-- Payment
+-- OutboxMessage
+-- Idempotency Result
|
COMMIT
```

RabbitMQ publication daha sonra yapılır.

Bu sayede veritabanı, payment processing ile messaging arasında durable handoff point haline gelir.

---

# 7. Outbox Message Modeli

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

Bu alanlar şunları mümkün kılar:

- durable event storage,
- retry scheduling,
- failure diagnostics,
- dead-letter handling,
- distributed worker ownership,
- lease renewal,
- crash recovery.

---

# 8. Atomic Outbox Claiming

Birden fazla API instance aynı anda çalışabilir. Bu nedenle iki worker aynı Outbox satırını aynı anda işlememelidir.

SQL Server implementasyonu şu locking hint'lerini kullanır:

```sql
UPDLOCK
READPAST
ROWLOCK
```

ve CTE tabanlı `UPDATE ... OUTPUT` claim query'si uygular.

```text
Worker A                 Worker B
   |                        |
   | row #1 claim           |
   |----------------------->|
   |                        |
   | row locked             |
   |                        | claim dener
   |                        | locked row'u skip eder
```

Bu yaklaşım klasik `SELECT -> UPDATE` race window'unu ortadan kaldırır.

---

# 9. Lease / Ownership Modeli

Worker bir Outbox message claim ettiğinde `LockedBy` ve `LockedUntilUtc` alanlarını yazar.

```text
LockedBy       = worker-instance-id
LockedUntilUtc = now + 60 saniye
```

Worker yalnızca lease geçerli olduğu sürece message'ın owner'ıdır.

## 9.1 Heartbeat

```text
Lease duration     = 60 saniye
Heartbeat interval = 20 saniye
Publish timeout    = 30 saniye
```

Heartbeat şu metodu çağırır:

```csharp
RenewLockAsync(...)
```

Lease renewal başarısız olursa worker ownership'ı kaybetmiş kabul edilir ve aktif publish attempt iptal edilir.

## 9.2 Ownership-Safe Finalization

`MarkAsProcessedAsync` için örneğin:

```text
Id eşleşmeli
ProcessedAtUtc null olmalı
DeadLetteredAtUtc null olmalı
LockedBy == current worker
```

Hiç satır update edilmezse `OutboxOwnershipLostException` fırlatılır. Bu, stale worker'ın ownership başka worker'a geçmişken row'u finalize etmesini engeller.

---

# 10. Retry, Backoff ve Jitter

Transient failure'lar retry edilir.

```text
Retry 1  -> 2 saniye
Retry 2  -> 5 saniye
Retry 3  -> 15 saniye
Retry 4  -> 30 saniye
Retry 5  -> 1 dakika
Retry 6  -> 2 dakika
Retry 7  -> 5 dakika
Retry 8+ -> 10 dakika
```

Aynı anda çok sayıda message'ın tekrar denenmesi sonucu oluşabilecek retry storm'u azaltmak için yaklaşık `0-5 saniye` jitter eklenir.

Retry state şu alanlarla tutulur:

```text
RetryCount
LastError
NextAttemptAtUtc
```

---

# 11. Failure Classification

Transient örnekler:

- broker unavailable,
- connection refused,
- network interruption,
- closed connection,
- I/O failure.

Permanent / recover edilemeyen örnekler:

- malformed payload,
- unsupported event type,
- permanent publish failure.

Geçici infrastructure problemleri doğrudan dead-letter edilmez; retry edilir.

---

# 12. RabbitMQ Publisher Confirms

Publishing yalnızca `BasicPublishAsync` çağrısıyla tamamlanmış kabul edilmez. Producer broker confirmation bekler.

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

Publisher confirm, broker'ın message'ı kabul ettiği anlamına gelir; downstream consumer'ın message'ı işlediği anlamına gelmez.

---

# 13. Mandatory Publishing ve Topology

Mevcut exchange:

```text
payment.events
```

Örnek routing key:

```text
payment.completed.*
```

Mandatory publishing, unroutable message senaryolarının görünür hale gelmesini sağlar.

---

# 14. RabbitMQ Connection Management

Her event için yeni TCP connection açmak yerine connection reuse edilir.

- singleton connection reuse,
- `SemaphoreSlim` synchronization,
- automatic recovery,
- topology recovery,
- lifecycle logging.

```text
AutomaticRecoveryEnabled = true
TopologyRecoveryEnabled  = true
NetworkRecoveryInterval  = 5 saniye
```

---

# 15. Outbox Publisher Worker

```text
loop
 |
 +--> atomic claim
 |
 +--> lease oluştur
 |
 +--> heartbeat başlat
 |
 +--> event publish et
 |
 +--> broker confirm bekle
 |
 +--> heartbeat durdur
 |
 +--> Outbox'ı processed işaretle
 |
 +--> sleep
```

Worker, RabbitMQ availability durumunu original HTTP request'ten ayırarak failure durumlarında retry mekanizmasını yönetir.

---

# 16. Publish Timeout

Mevcut timeout:

```text
30 saniye
```

```text
publish tamamlanmaz
        |
        v
publish timeout cancellation
        |
        v
heartbeat durdurulur
        |
        v
message failed olarak işaretlenir
        |
        v
retry schedule edilir
```

Bu senaryo controlled artificial delay kullanılarak test edilmiştir. Delay kaldırıldıktan sonra aynı durable Outbox message başarıyla retry edilmiştir.

---

# 17. Graceful Shutdown

Shutdown operational failure'dan farklı ele alınır.

```text
host cancellation
        |
        v
aktif publish iptal
        |
        v
worker güvenli şekilde çıkar
```

Deployment veya Ctrl+C işlemi retry counter'ı yanlış artırmamalı ve sahte RabbitMQ failure state'i üretmemelidir. Message lease süresi dolduktan sonra recover edilebilir ve sonraki worker tarafından tekrar claim edilebilir.

---

# 18. Notification Consumer

Ayrı bir servis vardır:

```text
Payment.NotificationWorker
```

Sorumlulukları:

- RabbitMQ consumption,
- manual ACK,
- retry handling,
- DLQ handling,
- prefetch control,
- consumer idempotency,
- graceful shutdown.

---

# 19. Consumer-Owned Queues

Her consuming service kendi queue'suna sahip olmalıdır.

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

Bu yapı service autonomy sağlar ve aynı event'e birden fazla consumer'ın bağımsız tepki vermesine izin verir.

---

# 20. Consumer Inbox / Idempotent Consumer

RabbitMQ at-least-once delivery sistemi olarak ele alınır; duplicate delivery mümkündür.

Notification Worker persistent Inbox table kullanır:

```text
ProcessedMessages
```

Önemli alanlar:

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

Consumer business side effect uygulamadan önce event'i kontrol eder / reserve eder.

Olası reservation sonuçları:

```text
Acquired
AlreadyProcessed
InProgress
```

### AlreadyProcessed

Business effect daha önce tamamlandığı için message güvenle ACK edilir.

### InProgress

Event aynı anda ikinci kez process edilmek yerine retry path üzerinden geciktirilir.

---

# 21. Consumer Retry Queue ve TTL

Notification Worker delayed retry queue kullanır.

Mevcut retry delay:

```text
5 saniye
```

TTL dolunca RabbitMQ message'ı yeniden processing queue'ya yönlendirir. Bu yaklaşım tight retry loop'larını engeller.

---

# 22. Dead Letter Queue

Retry policy kapsamında işlenemeyen message'lar DLQ'ya taşınabilir.

DLQ şu amaçlarla kullanışlıdır:

- manuel inceleme,
- alerting,
- replay,
- debugging,
- forensic analysis.

---

# 23. Manual ACK

Consumer acknowledgement manual olarak yapılır.

Message yalnızca:

```text
handler tamamlanır
+
Inbox state processed olarak persist edilir
```

sonrasında ACK edilir.

Bu sayede consumer handling sırasında crash olsa bile message kaybolmaz.

---

# 24. At-Least-Once Delivery Modeli

Sistem bilinçli olarak:

```text
At-least-once delivery
```

hedefler.

SQL Server + RabbitMQ üzerinde generic exactly-once delivery iddiasında bulunulmaz.

Reliable business effects şu kombinasyonla sağlanır:

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

API şu header'ı destekler:

```http
X-Correlation-ID
```

Client göndermiyorsa application üretir.

Correlation ID integration event'lere propagate edilir. Böylece şu zincirin tamamı ilişkilendirilebilir:

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

Sistem `ILogger` structured logging kullanır.

```csharp
_logger.LogInformation(
    "Payment transaction committed. PaymentId: {PaymentId}, EventId: {EventId}",
    payment.Id,
    eventId);
```

Structured property'ler merkezi logging sistemlerinde indexlenebilir ve filtrelenebilir.

---

# 27. Health Checks

API şu endpoint'leri expose eder:

```text
/health/live
/health/ready
```

## Liveness

Şu soruya cevap verir:

> Process ayakta mı?

## Readiness

Şu soruya cevap verir:

> Bu instance şu anda görevini yerine getirebilir durumda mı?

Readiness check'leri SQL Server ve RabbitMQ'yu içerir.

Test edilen davranış:

```text
RabbitMQ down
/health/live  -> 200
/health/ready -> 503
```

Broker tekrar ayağa kalktıktan sonra:

```text
/health/ready -> 200
```

---

# 28. OpenTelemetry ve Prometheus

Projede şu bileşenler kullanılır:

```text
System.Diagnostics.Metrics
OpenTelemetry
Prometheus exporter
```

Metrics şu endpoint'ten expose edilir:

```text
/metrics
```

Service metadata:

```text
service_name="Payment.Api"
```

---

# 29. Payment Metrics

Custom metric'ler:

```text
payment_started_total
payment_succeeded_total
payment_failed_total
payment_amount_currency
payment_duration_seconds
```

## payment_started_total

Payment processing attempt sayısını tutar.

Tag'ler:

```text
provider
currency
```

## payment_succeeded_total

Final payment transaction başarılı commit olduktan sonra increment edilir.

## payment_failed_total

Provider tarafından dönen business failure / decline durumlarını temsil eder. Technical failure'lar business decline metric'inden kavramsal olarak ayrıdır.

## payment_amount_currency

Successful payment amount'larını histogram olarak tutar.

## payment_duration_seconds

Application use case başlangıcından final transaction commit'e kadar end-to-end payment processing latency ölçer.

Yaklaşık olarak şunları kapsar:

```text
Payment creation
Provider call
Payment state transition
Outbox creation
Database persistence
Transaction commit
```

Successful Mock payment testinde yaklaşık:

```text
0.836 saniye
```

ölçülmüştür.

---

# 30. Outbox Metrics

Custom Outbox metric'leri:

```text
outbox_publish_succeeded_total
outbox_publish_failed_total
outbox_publish_timeout_total
outbox_ownership_lost_total
outbox_pending_messages
```

## outbox_publish_succeeded_total

RabbitMQ publish başarılı olduktan ve Outbox row processed olarak güvenle finalize edildikten sonra increment edilir.

## outbox_publish_failed_total

Failed publish **attempt** sayısını tutar; failed payment sayısını değil.

`failure_kind` örnekleri:

```text
transient
permanent
bad_payload
unsupported_event
unexpected
```

Aynı Outbox message retry nedeniyle bu counter'ı birden fazla kez artırabilir.

## outbox_publish_timeout_total

Configured publish timeout nedeniyle iptal edilen publish attempt'leri sayar.

## outbox_ownership_lost_total

Worker'ın claim edilmiş Outbox message ownership'ını kaybettiği durumları sayar.

## outbox_pending_messages

Mevcut unfinished Outbox backlog'u gösteren ObservableGauge'dur.

Pending tanımı:

```text
ProcessedAtUtc IS NULL
AND
DeadLetteredAtUtc IS NULL
```

Message şu durumlarda da pending kabul edilir:

- şu anda locked ise,
- `NextAttemptAtUtc` bekliyorsa,
- daha önce bir veya daha fazla kez failure yaşadıysa.

---

# 31. Metric Cardinality Stratejisi

High-cardinality değerler metric tag'lerinden özellikle uzak tutulur.

Uygun tag'ler:

```text
provider
currency
status
event_type
failure_kind
```

Kaçınılan metric tag'leri:

```text
PaymentId
OrderId
EventId
CorrelationId
TransactionId
```

Bu identifier'lar metrics yerine logs ve traces içinde tutulmalıdır.

---

# 32. Uygulanan Failure-Oriented Testler

Sistem bilinçli olarak hata senaryoları altında test edilmiştir.

## 32.1 Outbox Publish Sırasında RabbitMQ Down

Senaryo:

```text
API running
RabbitMQ stopped
new payment created
```

Gözlenen davranış:

```text
payment başarılı
Outbox durable kalır
publish transient failure alır
retry state artar
```

Metric:

```text
outbox_publish_failed_total{failure_kind="transient"}
```

RabbitMQ restart sonrası:

```text
Outbox retry eder
publish başarılı olur
message finalize edilir
```

## 32.2 Publish Timeout

30 saniyelik timeout'tan uzun geçici artificial delay eklendi.

```text
publish cancellation
message failed olarak işaretlendi
retry schedule edildi
```

Metric:

```text
outbox_publish_timeout_total
```

Artificial delay kaldırıldıktan sonra durable message başarıyla retry edildi.

## 32.3 Ownership Loss

Lease renewal bilinçli olarak fail ettirildi.

```text
heartbeat ownership loss tespit etti
leaseLostCancellation tetiklendi
publish attempt iptal edildi
unsafe DB finalization engellendi
```

Metric:

```text
outbox_ownership_lost_total
```

## 32.4 Active Publish Sırasında Graceful Shutdown

API active Outbox processing sırasında durduruldu.

```text
shutdown cancellation
!= broker failure
```

Message yanlışlıkla RabbitMQ failure olarak işaretlenmedi ve lease expire olduktan sonra recover edilebilir durumda kaldı.

## 32.5 RabbitMQ Readiness Failure

```text
/health/live  = 200
/health/ready = 503
```

Restart sonrası:

```text
/health/ready = 200
```

## 32.6 Consumer Shutdown / Restart

Notification Worker active delivery sırasında kapatılıp yeniden başlatıldı. Controlled shutdown'ın unsafe duplicate handling üretmediği test edildi.

## 32.7 Consumer InProgress Reservation

Inbox reservation başka processing attempt'in hâlâ aktif olduğunu gösterdiğinde:

```text
reservation result = InProgress
```

event aynı anda iki kez işlenmek yerine delay/retry path'e yönlendirildi.

---

# 33. SQL Server Persistence

Mevcut persistence provider:

```text
SQL Server
```

Örnek local config:

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

Projede EF migrations yerine manual SQL schema script yaklaşımı kullanılmaktadır. EF Core ORM olarak kullanılmaya devam eder.

---

# 34. Database Provider Esnekliği

Tasarım gelecekte şu provider'lara alan bırakır:

```text
SQL Server
PostgreSQL
MySQL
InMemory
```

Ancak atomic Outbox claim logic provider-specific'tir.

Mevcut robust implementasyon SQL Server'a özeldir. Gelecekte PostgreSQL/MySQL adapter'ları SQL Server hint'lerini kopyalamak yerine kendi native locking semantics'lerini kullanmalıdır.

---

# 35. InMemory Adapter'lar

Projede seçili abstraction'lar için InMemory implementasyonlar bulunur.

Örneğin:

```text
InMemoryPaymentRepository
InMemoryOutboxStore
```

Kullanım amaçları:

- test,
- local experimentation,
- infrastructure-independent development.

InMemory ve SQL-backed implementasyonlar aynı application contract'larını izler.

---

# 36. RabbitMQ Local Development

Mevcut local setup:

```text
Container: payment-rabbitmq
Image: rabbitmq:management
AMQP host port: 5673
Management UI: 15672
```

Yararlı komutlar:

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

# 37. Configuration ve Secrets

Örnek local RabbitMQ config:

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

Production secret'ları GitHub'a commit edilmemelidir.

Gerçek production ortamında environment variable veya secret manager kullanılmalıdır.

---

# 38. Consistency Model

Sistem şu yaklaşımı birleştirir:

```text
strong local transaction consistency
+
eventual consistency across services
```

Payment database içindeki kritik local state transactionally koordine edilir.

RabbitMQ ve downstream servisler arasında consistency eventual'dır.

---

# 39. RabbitMQ Neden SQL Transaction İçinde Değil?

External network call'ların uzun süren SQL transaction içinde tutulması doğru değildir.

Aksi halde:

- uzun süre tutulan lock'lar,
- deadlock,
- düşük throughput,
- infrastructure coupling,
- karmaşık rollback semantics

oluşabilir.

Final durable payment sonucu ve Outbox event önce local olarak commit edilir. RabbitMQ delivery daha sonra asenkron gerçekleşir.

---

# 40. Observability Felsefesi

Projede şu kavramlar birbirinden ayrılır:

```text
Logs
Metrics
Health
Correlation
```

### Logs şu soruya cevap verir:

> Ne oldu?

### Metrics şu soruya cevap verir:

> Kaç kez oldu, ne kadar oldu, ne kadar yavaş?

### Health checks şu soruya cevap verir:

> Servis şu anda görevini yerine getirebilir durumda mı?

### Correlation ID şu soruya cevap verir:

> Hangi distributed operation'lar aynı request'e ait?

Tracing bunun doğal sonraki adımıdır.

---

# 41. Bilinen Mühendislik Konuları

## 41.1 Claimed Batch'in Sequential İşlenmesi

Worker şu anda bir batch claim edip message'ları sequential işler.

Şu durumda subtle correctness/performance konusu vardır:

```text
batchSize = 20
```

20 message upfront claim edilir; ancak ilerideki message'ların heartbeat'i o message processing loop'a gelene kadar başlamaz.

Bu yüzden daha sonraki lease'ler, önceki message'lar işlenirken expire süresine yaklaşabilir.

İyileştirme seçenekleri:

```text
strict sequential semantics için batchSize = 1
```

veya:

```text
bounded parallelism
+
per-message scope
+
per-message heartbeat
```

High-throughput production kullanımından önce yeniden ele alınmalıdır.

## 41.2 Outbox Pending Gauge

`outbox_pending_messages`, Outbox Worker tarafından refresh edilen cached observable value kullanır.

Bunun nedeni ObservableGauge callback içinde asynchronous database access yapılmamasıdır.

## 41.3 Prometheus Endpoint Exposure

`/metrics` local development için uygundur.

Production ortamında genellikle:

- private networking,
- ingress rules,
- authentication,
- internal scraping configuration

ile korunmalıdır.

## 41.4 OpenTelemetry Export Stratejisi

Proje şu anda Prometheus-compatible metrics'i doğrudan expose eder.

Daha büyük ortamlarda OpenTelemetry Collector + OTLP export daha esnek bir mimari sağlayabilir.

---

# 42. Önerilen Sonraki Mühendislik Adımları

1. `outbox_pending_messages` gauge davranışını tamamla ve doğrula.
2. Business failure'dan ayrı payment technical-error metric ekle.
3. Provider bazlı latency analizi ekle.
4. Oldest pending Outbox age gauge ekle.
5. Outbox retry distribution metric'leri ekle.
6. Notification Worker metric'leri ekle.
7. `ActivitySource` ile OpenTelemetry tracing ekle.
8. RabbitMQ header'ları üzerinden trace context propagate et.
9. OpenTelemetry Collector ekle.
10. Grafana dashboard'ları ekle.
11. Prometheus alert rule'ları ekle.
12. Automated integration test projesi ekle.
13. RabbitMQ için Testcontainers ekle.
14. SQL Server için Testcontainers ekle.
15. Broker-down, timeout, retry, shutdown ve recovery testlerini automate et.
16. Outbox batch concurrency stratejisini yeniden değerlendir.
17. Provider-specific atomic claim adapter'ları ekle.
18. Gerçek virtual POS provider'ları ekle.
19. Production secrets management ekle.

---

# 43. Önerilen Production Alert'leri

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

# 44. Mühendislik Özeti

Projede şu yapılar uygulanmakta veya gösterilmektedir:

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

Projenin en önemli mimari özelliklerinden biri, failure'ların nadir edge-case olarak değil sistemin doğal çalışma koşullarından biri olarak ele alınmasıdır.

Sistem açık şekilde şu durumları modellemektedir:

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

# 45. Uçtan Uca Reliability Zinciri

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

Bu mimari aşağıdaki riskleri ciddi şekilde azaltır:

- duplicate payment,
- lost event,
- duplicate consumer side effect,
- unsafe retry behavior,
- inconsistent payment/message state,
- shutdown sırasında message loss,
- concurrent worker conflict,
- görünmeyen infrastructure failure.

---

## Proje Durumu

Proje aktif geliştirme aşamasındadır.

Mevcut odak; gerçek payment provider entegrasyonlarına ve production deployment detaylarına geçmeden önce reliability engineering, Outbox correctness, failure recovery ve observability katmanlarını sağlamlaştırmaktır.
