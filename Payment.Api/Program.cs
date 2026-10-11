
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Payment.Api.BackgroundServices;
using Payment.Api.Exceptions;
using Payment.Api.HealthChecks;
using Payment.Api.Middleware;
using Payment.Api.Observability;
using Payment.Application.Abstractions.Observability;
using Payment.Application.DependencyInjection;
using Payment.Infrastructure.DependencyInjection;
using Payment.Messaging.Abstractions.Publishing;
using Payment.Persistence.DependencyInjection;
using System.Text.Json;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICorrelationContext,HttpCorrelationContext>();
builder.Services.AddHostedService<OutboxPublisherWorker>();
builder.Services.AddSingleton<IPaymentMetrics,PaymentMetrics>();
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource =>
    {
        resource.AddService(
            serviceName: "Payment.Api");
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddMeter(PaymentMetrics.MeterName)
            .AddPrometheusExporter();
    });

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout =
        TimeSpan.FromSeconds(30);
});

builder.Services
    .AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: new[] { "live" })
    .AddCheck<SqlServerHealthCheck>(
        "sqlserver",
        tags: new[] { "ready" })
    .AddCheck<RabbitMqHealthCheck>(
        "rabbitmq",
        tags: new[] { "ready" });

Console.WriteLine(
    $"Messaging Provider: {builder.Configuration["Messaging:Provider"]}");
var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

using (var scope = app.Services.CreateScope())
{
    var eventPublisher = scope.ServiceProvider
        .GetRequiredService<IIntegrationEventPublisher>();

    Console.WriteLine(
        $"IIntegrationEventPublisher Implementation: {eventPublisher.GetType().FullName}");
}
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapPrometheusScrapingEndpoint();
app.MapHealthChecks(

    "/health/live",
    new HealthCheckOptions
    {
        Predicate = check =>
            check.Tags.Contains("live"),
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] =
                StatusCodes.Status200OK,

            [HealthStatus.Degraded] =
                StatusCodes.Status200OK,

            [HealthStatus.Unhealthy] =
                StatusCodes.Status503ServiceUnavailable
        },
        ResponseWriter =
            WriteHealthResponseAsync
    });

app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate = check =>
            check.Tags.Contains("ready"),

        ResultStatusCodes =
        {
            [HealthStatus.Healthy] =
                StatusCodes.Status200OK,

            [HealthStatus.Degraded] =
                StatusCodes.Status200OK,

            [HealthStatus.Unhealthy] =
                StatusCodes.Status503ServiceUnavailable
        },

        ResponseWriter =
            WriteHealthResponseAsync
    });

app.Run();


static async Task WriteHealthResponseAsync(
    HttpContext context,
    HealthReport report)
{
    context.Response.ContentType =
        "application/json";

    var response =
        new
        {
            status =
                report.Status.ToString(),

            totalDurationMs =
                report.TotalDuration.TotalMilliseconds,

            checks =
                report.Entries.Select(
                    entry => new
                    {
                        name =
                            entry.Key,

                        status =
                            entry.Value.Status.ToString(),

                        description =
                            entry.Value.Description,

                        durationMs =
                            entry.Value.Duration.TotalMilliseconds
                    })
        };

    await context.Response.WriteAsync(
        JsonSerializer.Serialize(
            response));
}