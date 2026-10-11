using Payment.Contracts.Events.Payments;
using Payment.Messaging.Abstractions.Consuming;
using Payment.NotificationWorker;
using Payment.NotificationWorker.Handlers;
using Payment.NotificationWorker.Messaging.RabbitMq;
using Payment.NotificationWorker.Idempotency;
using Microsoft.EntityFrameworkCore;
using Payment.NotificationWorker.Persistence.Database;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq"));

builder.Services.AddScoped<
    IIntegrationEventHandler<PaymentCompletedEvent>,
    PaymentCompletedEventHandler>();
builder.Services.AddHostedService<
    RabbitMqTopologyInitializer>();
builder.Services.AddScoped<
    IProcessedMessageStore,
    EfCoreProcessedMessageStore>();

builder.Services.AddDbContext<NotificationDbContext>(
    options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString(
                "NotificationDatabase")));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

host.Run();