using Payment.Application.Abstractions.Persistence;
using Payment.Persistence.Repositories.InMemory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Payment.Application.Abstractions.Idempotency;
using Payment.Application.Abstractions.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Payment.Persistence.Database;
using Payment.Persistence.Idempotency.InMemory;
using Payment.Persistence.Outbox.InMemory;
using Payment.Persistence.Repositories.EfCore;
using Payment.Persistence.Idempotency.EfCore;
using Payment.Persistence.Outbox.EfCore;
using Payment.Persistence.UnitOfWork.InMemory;
using Payment.Persistence.UnitOfWork.EfCore;

namespace Payment.Persistence.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
        {
            var provider =
                configuration["Persistence:Provider"]
                ?? "InMemory";

            // ==============================================
            // IN MEMORY
            // ==============================================

            if (provider.Equals(
                "InMemory",
                StringComparison.OrdinalIgnoreCase))
            {
                services.AddSingleton<
                    IPaymentRepository,
                    InMemoryPaymentRepository>();

                services.AddSingleton<
                    IPaymentIdempotencyStore,
                    InMemoryPaymentIdempotencyStore>();

                services.AddSingleton<
                    IOutboxStore,
                    InMemoryOutboxStore>();
                services.AddSingleton<
                    IUnitOfWork,
                    InMemoryUnitOfWork>();
                return services;
            }

            // ==============================================
            // SQL SERVER
            // ==============================================

            if (provider.Equals(
                "SqlServer",
                StringComparison.OrdinalIgnoreCase))
            {
                services.AddDbContext<PaymentDbContext>(
                    options =>
                    {
                        options.UseSqlServer(
                            configuration.GetConnectionString(
                                "PaymentDatabase"));
                    });

                services.AddScoped<
                    IPaymentRepository,
                    EfCorePaymentRepository>();

                services.AddScoped<
                    IPaymentIdempotencyStore,
                    EfCorePaymentIdempotencyStore>();

                services.AddScoped<
                    IOutboxStore,
                    EfCoreOutboxStore>();
                services.AddScoped<
                    IUnitOfWork,
                    EfCoreUnitOfWork>();
                return services;
            }

            throw new InvalidOperationException(
                $"Unsupported persistence provider: {provider}");
        }
    }
}
