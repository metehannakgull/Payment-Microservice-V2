# Payment System

Modern, modüler, genişletilebilir ve yüksek güvenilirlik hedefiyle geliştirilen bir ödeme altyapısı.

Projenin temel amacı yalnızca çalışan bir ödeme API'si oluşturmak değil; gerçek dünya ödeme sistemlerinde kullanılan mimari yaklaşımları uygulayarak aşağıdaki özelliklere sahip bir altyapı oluşturmaktır:

- Ödeme sağlayıcılarının kolay değiştirilebilmesi
- Mesajlaşma altyapısının RabbitMQ, Kafka vb. teknolojilere uyarlanabilmesi
- SQL Server / PostgreSQL gibi farklı persistence teknolojilerine geçilebilmesi
- Duplicate payment ve duplicate event problemlerinin engellenmesi
- Message broker geçici olarak kapalı olsa bile event kaybının önlenmesi
- Retry, Dead Letter Queue ve Outbox mekanizmalarının kullanılması
- Correlation ID ile distributed request takibi
- Structured logging ve observability altyapısının hazırlanması
- Microservice mimarisine uygun servis sınırlarının oluşturulması
- Gerçek sanal POS / banka entegrasyonlarına hazır bir temel oluşturulması

---

## 1. Projenin Temel Amacı

Ödeme sistemlerinde yalnızca:

```text
POST /payment
→ banka
→ başarılı
```

şeklinde çalışan basit bir API yeterli değildir.

Gerçek sistemlerde aşağıdaki problemler dikkate alınmalıdır:

```text
Client aynı ödeme isteğini iki kez gönderirse ne olacak?

RabbitMQ aynı mesajı iki kez teslim ederse ne olacak?

Ödeme başarılı olduktan sonra RabbitMQ çökerse event kaybolacak mı?

Consumer event'i işlerken hata verirse ne olacak?

RabbitMQ geçici olarak kapalıysa ne olacak?

Sistem restart olduğunda pending event'ler korunacak mı?

Bir işlemin API → RabbitMQ → Worker zincirini nasıl takip edeceğiz?

RabbitMQ yerine Kafka kullanmak istersek Application kodunu değiştirmek gerekecek mi?

SQL Server yerine PostgreSQL kullanmak istersek business logic etkilenecek mi?
```

Proje bu problemlerin tamamını mimari olarak çözebilecek şekilde geliştirilmektedir.

---

## 2. Kullanılan Teknolojiler

Şu anda kullanılan temel teknoloji seti:

```text
.NET 8
ASP.NET Core Web API
C#
RabbitMQ
Docker
Worker Service
Microsoft.Extensions.Hosting
Microsoft.Extensions.Logging
System.Text.Json
Swagger / OpenAPI
```

Şu anda persistence için gerçek bir veritabanı yerine:

```text
InMemory Repository
ConcurrentDictionary
```

kullanılmaktadır.

Bu bilinçli bir tercihtir.

Persistence abstraction hazırlandığı için ileride:

```text
SQL Server
PostgreSQL
```

gibi sistemlere geçilecektir.

---

## 3. Solution Yapısı

```text
Solution
│
├── BuildingBlocks
│   │
│   ├── Payment.Contracts
│   │   ├── Common
│   │   └── Events
│   │       └── Payments
│   │
│   └── Payment.Messaging.Abstractions
│       ├── Publishing
│       └── Consuming
│
├── Services
│   │
│   ├── Payment
│   │   │
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

---

## 4. Kullanılan Ana Mimari

Projede temel olarak:

```text
Clean Architecture
+
Hexagonal Architecture
+
Ports & Adapters
+
Microservice Architecture
+
Event-Driven Architecture
```

yaklaşımları birlikte kullanılmaktadır.

Amaç business logic'in:

```text
RabbitMQ
SQL Server
HTTP
Docker
Kafka
PostgreSQL
```

gibi altyapı teknolojilerine doğrudan bağımlı olmamasıdır.

---

## 5. Katmanlar

### Payment.Domain

Domain katmanı sistemin temel business modelini içerir.

Örneğin:

```text
Payment
PaymentStatus
Currency
PaymentProviderType
```

Payment entity içerisinde business davranışları bulunmaktadır.

```csharp
payment.MarkAsProcessing();
payment.MarkAsSuccessful();
payment.MarkAsFailed(...);
```

Bu sayede ödeme durum değişiklikleri controller veya infrastructure içerisinde değil Domain tarafından yönetilmektedir.

### Payment.Application

Application katmanı use-case'leri yönetir.

Örneğin:

```text
CreatePaymentService
```

Application aşağıdaki abstraction'ları kullanır:

```text
IPaymentRepository
IPaymentProvider
IPaymentProviderFactory
IPaymentIdempotencyStore
IOutboxStore
ICorrelationContext
```

Application:

```text
RabbitMQ nasıl çalışıyor?
SQL Server nasıl bağlanıyor?
HTTP request nasıl geliyor?
```

gibi detayları bilmez.

### Payment.Infrastructure

Infrastructure dış sistem adaptörlerini içerir.

```text
RabbitMqIntegrationEventPublisher
RabbitMqConnection
MockPaymentProvider
PaymentProviderFactory
```

Burada Adapter Pattern uygulanmaktadır.

### Payment.Persistence

Persistence katmanı veri saklama implementasyonlarını içerir.

Şu anda:

```text
InMemoryPaymentRepository
InMemoryPaymentIdempotencyStore
InMemoryOutboxStore
```

kullanılmaktadır.

İleride:

```text
SqlServerPaymentRepository
PostgreSqlPaymentRepository
EfCoreOutboxStore
EfCoreIdempotencyStore
```

eklenebilecek şekilde tasarlanmıştır.

---

## 6. Payment Provider Abstraction

Ödeme sağlayıcılarını Application katmanından ayırmak için:

```text
IPaymentProvider
```

oluşturuldu.

Provider seçimi için:

```text
IPaymentProviderFactory
```

kullanılmaktadır.

Mevcut implementasyon:

```text
MockPaymentProvider
```

şeklindedir.

İleride örneğin:

```text
GarantiPaymentProvider
IsBankPaymentProvider
QnbPaymentProvider
PayTRPaymentProvider
```

eklenebilir.

Application Service yalnızca:

```csharp
var provider = _paymentProviderFactory
    .GetProvider(command.Provider);
```

şeklinde provider ister.

Burada:

```text
Strategy Pattern
Factory Pattern
Adapter Pattern
Dependency Inversion
```

yaklaşımları kullanılmaktadır.

---

## 7. Repository Pattern

Payment persistence için:

```text
IPaymentRepository
```

oluşturuldu.

Şu anda implementasyon:

```text
InMemoryPaymentRepository
```

şeklindedir.

Veriler:

```text
ConcurrentDictionary
```

içerisinde tutulmaktadır.

Bu nedenle uygulama restart olduğunda veriler silinir. Bu, development aşamasında kullanılan geçici persistence çözümüdür.

---

## 8. Messaging Abstraction

Application'ın RabbitMQ'ya doğrudan bağımlı olmaması için:

```text
IIntegrationEventPublisher
```

oluşturuldu.

Örneğin:

```csharp
public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(
        TEvent integrationEvent,
        CancellationToken cancellationToken = default)
        where TEvent : class;
}
```

RabbitMQ implementasyonu:

```text
RabbitMqIntegrationEventPublisher
```

şeklindedir.

İleride:

```text
KafkaIntegrationEventPublisher
AzureServiceBusIntegrationEventPublisher
```

gibi implementasyonlar eklenebilir.

RabbitMQ ve Kafka aynı teknoloji değildir.

RabbitMQ daha çok:

```text
Queue
Routing
ACK / NACK
Retry
DLQ
Work Queue
```

modelinde güçlüdür.

Kafka ise daha çok:

```text
Event Log
Partition
Offset
Consumer Group
Replay
High Throughput Streaming
```

modelinde çalışır.

Ancak `IIntegrationEventPublisher` abstraction'ı sayesinde Application mümkün olduğunca broker-independent tutulmaktadır.

---

## 9. Integration Event Contracts

Event contract'ları ayrı:

```text
Payment.Contracts
```

projesine taşındı.

Örneğin:

```text
PaymentCompletedEvent
PaymentFailedEvent
```

Bu sayede producer ve consumer aynı event contract'larını kullanabilmektedir.

---

## 10. Integration Event Metadata

Event'lere ortak metadata eklendi:

```text
EventId
CorrelationId
OccurredAtUtc
```

`EventId`, mesajın kendisinin benzersiz kimliğidir.

`CorrelationId`, aynı işlem zincirinin farklı sistemlerde takip edilmesini sağlar.

---

## 11. RabbitMQ Altyapısı

RabbitMQ Docker container olarak çalıştırılmaktadır.

Host mapping:

```text
5673 → RabbitMQ AMQP 5672
15672 → RabbitMQ Management UI
```

Management UI:

```text
http://localhost:15672
```

Container:

```text
payment-rabbitmq
```

RabbitMQ otomatik restart için:

```bash
docker update --restart unless-stopped payment-rabbitmq
```

kullanılmaktadır.

---

## 12. RabbitMQ Topology

Ana exchange:

```text
payment.events
```

Exchange type:

```text
topic
```

Payment Completed routing key:

```text
payment.completed
```

Notification consumer queue:

```text
notification.payment.completed
```

Retry queue:

```text
notification.payment.completed.retry
```

Dead Letter Queue:

```text
notification.payment.completed.dlq
```

Retry exchange:

```text
notification.retry
```

DLX:

```text
notification.dlx
```

---

## 13. Producer-Owned / Consumer-Owned Topology

Producer yalnızca kendi event exchange'ini bilir:

```text
Payment.Api
→ payment.events
```

Producer, consumer queue'larını oluşturmaz.

Queue consumer tarafından sahiplenilir.

Bu yaklaşım:

```text
Service Autonomy
Consumer-Owned Queue
Separation of Concerns
```

prensiplerine uygundur.

---

## 14. Notification Worker

Payment event'lerini tüketmek için ayrı:

```text
Payment.NotificationWorker
```

servisi oluşturuldu.

Akış:

```text
RabbitMQ
    ↓
notification.payment.completed
    ↓
Payment.NotificationWorker
    ↓
PaymentCompletedEventHandler
```

---

## 15. Manual ACK

RabbitMQ consumer:

```text
autoAck = false
```

olarak çalışmaktadır.

Mesaj yalnızca handler başarıyla tamamlandıktan sonra ACK edilmektedir.

```text
Message geldi
↓
Deserialize
↓
Handler
↓
Başarılı
↓
ACK
```

Handler hata verirse mesaj başarılı kabul edilmez.

---

## 16. Prefetch

Worker:

```text
prefetchCount = 1
```

ile çalışmaktadır.

Bu sayede consumer aynı anda kontrolsüz sayıda mesaj almaz.

---

## 17. RabbitMQ Retry Mekanizması

Consumer processing sırasında hata oluşursa mesaj kontrollü şekilde retry queue'ya gönderilmektedir.

Mesaj header'ında:

```text
x-retry-count
```

tutulmaktadır.

Akış:

```text
Main Queue
↓
Consumer hata
↓
Retry Exchange
↓
Retry Queue
↓
TTL
↓
payment.events
↓
Main Queue
```

Maksimum retry sonrasında mesaj DLQ'ya gönderilir.

---

## 18. RabbitMQ Dead Letter Queue

Consumer tarafında sürekli hata veren mesajlar:

```text
notification.payment.completed.dlq
```

queue'suna taşınmaktadır.

Bu sayede problemli mesaj sonsuz retry döngüsüne girmez.

---

## 19. At-Least-Once Delivery

RabbitMQ duplicate delivery yapabileceği için sistem:

```text
At-Least-Once
```

delivery modeline göre tasarlanmıştır.

Bu nedenle consumer'ın idempotent olması gerekir.

---

## 20. Consumer Idempotency

RabbitMQ aynı event'i tekrar teslim ederse aynı işlemin iki kez çalışmasını önlemek için:

```text
IProcessedMessageStore
```

oluşturuldu.

Development implementasyonu:

```text
InMemoryProcessedMessageStore
```

şeklindedir.

Akış:

```text
Message
↓
EventId kontrolü
↓
Daha önce işlendi
    → ACK
    → Handler çalışmaz

Daha önce işlenmedi
    → Handler
    → MarkAsProcessed
    → ACK
```

Bu yapı:

```text
Idempotent Consumer
Message Deduplication
Inbox Pattern foundation
```

olarak kullanılmaktadır.

---

## 21. Consumer Idempotency Testi

Aynı `EventId` iki kez gönderilerek test yapılmıştır.

İlk event işlendi.

İkinci event:

```text
Duplicate event ignored
```

şeklinde loglandı.

---

## 22. API-Level Idempotency

Messaging tarafındaki deduplication, client'ın aynı ödeme isteğini iki kez göndermesi problemini çözmez.

Bu nedenle:

```text
Idempotency-Key
```

HTTP header desteği eklendi.

Örneğin:

```http
Idempotency-Key: PAYMENT-TEST-001
```

---

## 23. Payment Idempotency Store

API-level idempotency için:

```text
IPaymentIdempotencyStore
```

oluşturuldu.

Development implementation:

```text
InMemoryPaymentIdempotencyStore
```

şeklindedir.

Aynı anda gelen iki request'in ikisinin birden ödeme başlatmaması için:

```text
ConcurrentDictionary.TryAdd()
```

ile atomic reservation uygulanmaktadır.

---

## 24. Request Hash

Aynı `Idempotency-Key` farklı payment data ile kullanılırsa bunu tespit etmek için request hash üretilmektedir.

Hash içerisinde:

```text
OrderId
Amount
Currency
Provider
```

kullanılmaktadır.

Algorithm:

```text
SHA-256
```

Aynı key farklı data ile tekrar kullanılırsa:

```text
409 Conflict
```

döndürülmektedir.

---

## 25. Idempotency Sonuç Cache'i

Aynı:

```text
Idempotency-Key
+
aynı request
```

tekrar gelirse provider ikinci kez çağrılmaz.

İlk payment sonucu tekrar döndürülür.

Böylece aynı:

```text
PaymentId
TransactionId
```

client'a geri verilir.

---

## 26. Global Exception Handling

Controller içerisinde sürekli `try/catch` yazmamak için global exception handler oluşturuldu.

.NET 8:

```text
IExceptionHandler
```

altyapısı kullanıldı.

Örneğin:

```text
IdempotencyConflictException
→ 409 Conflict

IdempotencyInProgressException
→ 409 Conflict

ArgumentException
→ 400 Bad Request

Unhandled Exception
→ 500 Internal Server Error
```

---

## 27. ProblemDetails

API hata response'ları standart:

```text
ProblemDetails
```

formatına geçirildi.

Örneğin:

```json
{
  "title": "Conflict",
  "status": 409,
  "detail": "The same Idempotency-Key cannot be used with different payment data.",
  "instance": "/api/Payments",
  "traceId": "..."
}
```

---

## 28. Correlation ID

Distributed işlemleri uçtan uca takip etmek için:

```text
X-Correlation-ID
```

header desteği eklendi.

Middleware:

```text
CorrelationIdMiddleware
```

oluşturuldu.

Client:

```http
X-Correlation-ID: CORR-TEST-12345
```

gönderirse aynı değer:

```text
Payment.Api
CreatePaymentService
PaymentCompletedEvent
RabbitMQ
NotificationWorker
```

zincirinde taşınmaktadır.

Client correlation ID göndermezse sistem otomatik yeni ID üretmektedir.

---

## 29. Correlation Abstraction

Application'ın doğrudan `HttpContext` bağımlılığı olmaması için:

```text
ICorrelationContext
```

oluşturuldu.

API implementation:

```text
HttpCorrelationContext
```

şeklindedir.

---

## 30. Structured Logging

Ödeme akışı log scope içerisinde tutulmaktadır.

API tarafında:

```text
CorrelationId
PaymentId
OrderId
Provider
```

Worker tarafında:

```text
CorrelationId
EventId
PaymentId
OrderId
```

scope kullanılmaktadır.

Bu yapı ileride:

```text
Seq
ELK
Grafana Loki
OpenTelemetry
```

gibi sistemlerle kolay entegre edilebilir.

---

## 31. Outbox Pattern

Başlangıçta payment başarılı olduktan sonra:

```text
CreatePaymentService
↓
RabbitMQ Publish
```

yapılıyordu.

Bu yapının problemi:

```text
Payment DB update başarılı
↓
RabbitMQ publish başarısız
↓
Payment başarılı
↓
Event kayıp
```

Bu problemi çözmek için Outbox Pattern eklendi.

Yeni yapı:

```text
CreatePaymentService
↓
Payment
↓
Integration Event
↓
OutboxMessage
```

Application artık RabbitMQ'ya direkt publish etmez.

---

## 32. IOutboxStore

Outbox persistence için:

```text
IOutboxStore
```

oluşturuldu.

Şu anda:

```text
InMemoryOutboxStore
```

kullanılmaktadır.

Outbox mesajı içerisinde:

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
```

gibi bilgiler tutulmaktadır.

---

## 33. OutboxPublisherWorker

Outbox'taki pending mesajları broker'a göndermek için:

```text
OutboxPublisherWorker
```

oluşturuldu.

Akış:

```text
CreatePaymentService
        ↓
Outbox
        ↓
OutboxPublisherWorker
        ↓
IIntegrationEventPublisher
        ↓
RabbitMQ
```

Buradaki önemli mimari ayrım:

```text
CreatePaymentService
```

mesaj broker'ını bilmez.

Broker seçimini:

```text
IIntegrationEventPublisher
```

abstraction'ı yönetir.

---

## 34. RabbitMQ / Kafka Geçiş Mantığı

Outbox Pattern'in amacı doğrudan RabbitMQ → Kafka geçişi değildir.

İki farklı sorumluluk vardır:

```text
IIntegrationEventPublisher
→ Messaging technology abstraction

Outbox Pattern
→ Reliable event publishing
```

Birlikte kullanıldığında:

```text
CreatePaymentService
        ↓
Outbox
        ↓
OutboxPublisher
        ↓
IIntegrationEventPublisher
        ↓
RabbitMQ / Kafka / başka broker
```

yapısı ortaya çıkar.

İleride configuration üzerinden:

```json
{
  "Messaging": {
    "Provider": "RabbitMq"
  }
}
```

veya:

```json
{
  "Messaging": {
    "Provider": "Kafka"
  }
}
```

seçimi yapılabilir.

---

## 35. Outbox Failure Testi

RabbitMQ manuel olarak kapatılarak test yapılmıştır:

```bash
docker stop payment-rabbitmq
```

RabbitMQ kapalıyken payment request gönderildi.

Payment API:

```text
200 OK
```

döndürdü.

Event Outbox içerisinde kaldı.

RabbitMQ tekrar:

```bash
docker start payment-rabbitmq
```

ile açıldığında pending event yeniden publish edildi ve NotificationWorker tarafından işlendi.

Böylece broker temporary outage durumunda event kaybının önüne geçildiği doğrulandı.

---

## 36. Outbox Retry / Exponential Backoff

RabbitMQ uzun süre kapalıysa her 2 saniyede bir sürekli bağlantı denemesi yapmak istemediğimiz için Outbox retry mekanizmasına backoff eklendi.

Retry planı:

```text
Retry 1 → 2 saniye
Retry 2 → 5 saniye
Retry 3 → 10 saniye
Retry 4 → 30 saniye
Retry 5+ → 60 saniye
```

OutboxMessage içerisinde:

```text
NextAttemptAtUtc
```

tutulmaktadır.

`GetPendingAsync` yalnızca:

```text
NextAttemptAtUtc <= DateTime.UtcNow
```

olan mesajları döndürür.

---

## 37. Outbox Dead Letter

Outbox event'lerinin sonsuza kadar retry edilmemesi için Outbox seviyesinde dead-letter mekanizması eklenmektedir.

Bu RabbitMQ DLQ'dan farklıdır.

```text
RabbitMQ DLQ
→ Mesaj broker'a ulaştı
→ Consumer işleyemedi

Outbox Dead Letter
→ Mesaj broker'a bile başarıyla gönderilemedi
```

Outbox mesajında:

```text
DeadLetteredAtUtc
DeadLetterReason
IsDeadLettered
```

alanları kullanılmaktadır.

Maksimum retry sonrasında mesaj dead-letter olarak işaretlenir ve otomatik retry durdurulur.

---

## 38. Şu Anki Uçtan Uca Akış

```text
CLIENT
  │
  │ POST /api/Payments
  │ Idempotency-Key
  │ X-Correlation-ID
  ▼
PAYMENT API
  │
  ├── API Idempotency Check
  │
  ├── Request Hash
  │
  ├── Atomic Idempotency Reservation
  │
  ▼
CREATE PAYMENT SERVICE
  │
  ├── Payment Entity
  │
  ├── Payment Provider Factory
  │
  ├── Provider.PayAsync()
  │
  ▼
PAYMENT RESULT
  │
  ├── PaymentCompletedEvent
  │       veya
  └── PaymentFailedEvent
          │
          ▼
        OUTBOX
          │
          ▼
OUTBOX PUBLISHER WORKER
          │
          ├── Retry
          ├── Backoff
          └── Dead Letter
          │
          ▼
IIntegrationEventPublisher
          │
          ▼
       RabbitMQ
          │
          ▼
payment.events
          │
          ▼
notification.payment.completed
          │
          ▼
NOTIFICATION WORKER
          │
          ├── EventId Deduplication
          ├── Consumer Idempotency
          ├── Handler
          ├── Retry Queue
          ├── DLQ
          └── ACK
```

---

## 39. Kullanılan Design Pattern ve Mimari Yaklaşımlar

```text
Clean Architecture
Hexagonal Architecture
Ports & Adapters
Microservice Architecture
Event-Driven Architecture
Repository Pattern
Strategy Pattern
Factory Pattern
Adapter Pattern
Dependency Injection
Dependency Inversion
Separation of Concerns
Single Responsibility Principle
Idempotent Consumer
Inbox Pattern Foundation
Outbox Pattern
Retry Pattern
Exponential Backoff
Dead Letter Queue
Message Deduplication
Correlation ID
Structured Logging
At-Least-Once Delivery
Consumer-Owned Queues
Service Autonomy
Integration Contracts
ProblemDetails
Global Exception Handling
Atomic Reservation
```

---

## 40. Güvenilirlik Katmanları

```text
HTTP Request
→ Idempotency-Key

Application
→ Request Hash

Messaging Producer
→ Outbox

Outbox Publisher
→ Retry + Backoff + Dead Letter

RabbitMQ
→ Durable Exchange / Queue

Consumer
→ Manual ACK

Consumer Processing
→ Retry Queue

Consumer Failure
→ RabbitMQ DLQ

Duplicate Delivery
→ EventId / Inbox / Idempotent Consumer

Distributed Tracking
→ Correlation ID
```

---

## 41. Development Ortamındaki Geçici InMemory Yapılar

Şu anda aşağıdaki yapılar InMemory çalışmaktadır:

```text
Payment Repository
Payment Idempotency Store
Processed Message Store
Outbox Store
```

Uygulama restart olduğunda:

```text
Payment kayıtları
Idempotency-Key kayıtları
Processed EventId kayıtları
Pending OutboxMessage kayıtları
```

kaybolur.

Bu production için yeterli değildir.

---

## 42. Transactional Outbox

Gerçek database'e geçildiğinde:

```text
Payment
+
OutboxMessage
```

aynı database transaction içerisinde yazılacaktır.

Örneğin:

```text
BEGIN TRANSACTION

INSERT PAYMENT

INSERT OUTBOX_MESSAGE

COMMIT
```

Bu:

```text
Transactional Outbox Pattern
```

olarak adlandırılır.

---

## 43. Transactional Inbox

Consumer tarafında da ileride:

```text
ProcessedMessage
```

database tablosuna taşınacaktır.

Örneğin:

```text
EVENT_ID
CONSUMER_NAME
PROCESSED_AT
```

ve:

```text
EventId + ConsumerName
```

üzerinde unique constraint kullanılacaktır.

---

## 44. Database Provider Hedefi

Persistence katmanının configuration üzerinden seçilebilir olması hedeflenmektedir.

```json
{
  "Persistence": {
    "Provider": "SqlServer"
  }
}
```

veya:

```json
{
  "Persistence": {
    "Provider": "PostgreSql"
  }
}
```

Application katmanı bu değişiklikten etkilenmeyecektir.

---

## 45. Messaging Provider Hedefi

Messaging altyapısı da configuration ile seçilebilir hale getirilecektir.

```json
{
  "Messaging": {
    "Provider": "RabbitMq"
  }
}
```

ileride:

```json
{
  "Messaging": {
    "Provider": "Kafka"
  }
}
```

kullanılabilecektir.

Transport-specific davranışlar kendi adapter'larında tutulacaktır.

---

## 46. Güvenlik Hedefleri

Gerçek payment provider entegrasyonlarında özellikle şu güvenlik kuralları uygulanacaktır:

```text
PAN / Card Number loglanmayacak
CVV hiçbir şekilde saklanmayacak
Sensitive payment data loglanmayacak
Secrets appsettings içerisinde tutulmayacak
Production secrets Secret Manager / Vault üzerinden alınacak
HTTPS / TLS kullanılacak
Provider callback signature doğrulaması yapılacak
API authentication eklenecek
Rate limiting uygulanacak
PCI-DSS kapsamı dikkate alınacak
```

---

## 47. Gerçek Payment Provider Entegrasyonları

İleride aşağıdaki yetenekler eklenebilir:

```text
Payment
3D Secure
Callback / Webhook
Payment Status Query
Refund
Partial Refund
Void
Cancel
Tokenization
Installment
Provider Error Mapping
Timeout Handling
Provider-Specific Retry Policy
```

---

## 48. Observability Yol Haritası

Mevcut sistem:

```text
ILogger
CorrelationId
Structured Logging Scope
```

kullanmaktadır.

İleride:

```text
OpenTelemetry
Distributed Tracing
Metrics
Health Checks
Prometheus
Grafana
Seq
ELK
Grafana Loki
```

eklenebilir.

---

## 49. Health Check Hedefi

Servisler için:

```text
/health
/health/live
/health/ready
```

endpoint'leri eklenmesi planlanmaktadır.

Kontrol edilecek bileşenler:

```text
Database
RabbitMQ
Payment Provider
Application
```

---

## 50. Ölçekleme

Mimari ileride horizontal scaling'e uygun olacak şekilde tasarlanmaktadır.

Örneğin:

```text
Payment.Api Instance 1
Payment.Api Instance 2
Payment.Api Instance 3
```

ve:

```text
NotificationWorker 1
NotificationWorker 2
NotificationWorker 3
```

çalışabilir.

Bu nedenle:

```text
Idempotency
Inbox
Unique Constraints
Atomic Operations
```

özellikle önemlidir.

---

## 51. Şu Ana Kadar Başarıyla Test Edilen Senaryolar

```text
Normal Payment Request
RabbitMQ publish
NotificationWorker consume
Manual ACK
Retry
Maximum Retry
RabbitMQ DLQ
Duplicate Event
Consumer Idempotency
API Idempotency
Same Key + Same Request
Same Key + Different Amount
409 Conflict
ProblemDetails
Correlation ID propagation
Structured Logging
Outbox publish
RabbitMQ offline sırasında Payment
Outbox pending message
RabbitMQ yeniden açıldığında automatic publish
Outbox Retry
Exponential Backoff
```

---

## 52. Mevcut Sistem Durumu

Şu anda sistem:

```text
Payment API
Payment Domain
Payment Application
Payment Provider Abstraction
InMemory Persistence
RabbitMQ Producer
RabbitMQ Consumer
Notification Worker
Retry Queue
DLQ
API Idempotency
Consumer Idempotency
Correlation ID
Structured Logging
Global Exception Handler
ProblemDetails
Outbox Pattern
Outbox Publisher
Outbox Retry
Exponential Backoff
Outbox Dead Letter Foundation
```

özelliklerini içermektedir.

---

## 53. Sonraki Büyük Adım

Bir sonraki önemli geliştirme:

```text
SQL Server / PostgreSQL
+
EF Core
+
Transactional Outbox
+
Persistent Idempotency
+
Persistent Inbox
```

olacaktır.

Bu aşamadan sonra uygulama restart olsa bile:

```text
Payment
Idempotency records
Outbox messages
Processed event records
```

kaybolmayacaktır.

---

## 54. Hedef Nihai Mimari

```text
                    CLIENT
                       │
                       ▼
                  PAYMENT API
                       │
          ┌────────────┴────────────┐
          │                         │
          ▼                         ▼
     Idempotency                 Logging
          │                         │
          ▼                         ▼
    Application Layer       Correlation / Trace
          │
          ▼
       Domain
          │
          ▼
   Payment Provider
          │
          ▼
   Database Transaction
          │
       ┌──┴─────────┐
       ▼            ▼
    PAYMENT       OUTBOX
                     │
                     ▼
              Outbox Publisher
                     │
                     ▼
          IIntegrationEventPublisher
                     │
        ┌────────────┴────────────┐
        ▼                         ▼
     RabbitMQ                   Kafka
        │
        ▼
      Queue
        │
        ▼
 Notification Worker
        │
        ▼
       Inbox
        │
        ▼
 Idempotent Handler
        │
        ▼
 Notification / Future Services
```

---

## 55. Projenin Genel Felsefesi

Bu projenin temel yaklaşımı:

> Business logic'i altyapı teknolojilerinden ayırmak, ödeme işlemlerinde duplicate ve message-loss risklerini azaltmak ve gerçek production ödeme sistemlerinde kullanılan reliability pattern'lerini adım adım uygulamak.

Amaç yalnızca çalışan bir sanal POS API'si oluşturmak değildir.

Amaç:

```text
ölçeklenebilir
test edilebilir
bakımı kolay
broker-independent
database-independent
resilient
observable
idempotent
event-driven
production-ready
```

bir ödeme altyapısı oluşturmaktır.
