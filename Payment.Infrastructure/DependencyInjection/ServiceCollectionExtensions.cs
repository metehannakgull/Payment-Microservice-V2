using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payment.Application.Abstractions.Payments;
using Payment.Infrastructure.Messaging.InMemory;
using Payment.Infrastructure.Messaging.RabbitMq;
using Payment.Infrastructure.PaymentProviders;
using Payment.Infrastructure.PaymentProviders.Mock;
using Payment.Messaging.Abstractions.Publishing;

namespace Payment.Infrastructure.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddScoped<
                IPaymentProvider,
                MockPaymentProvider>();

            services.AddScoped<
                IPaymentProviderFactory,
                PaymentProviderFactory>();

            var messagingProvider =
                configuration["Messaging:Provider"]
                ?? "InMemory";

            if (messagingProvider.Equals(
                "RabbitMq",
                StringComparison.OrdinalIgnoreCase))
            {
                services.Configure<RabbitMqOptions>(
                    configuration.GetSection(
                        RabbitMqOptions.SectionName));

                services.AddSingleton<RabbitMqConnection>();

                services.AddSingleton<
                    IIntegrationEventPublisher,
                    RabbitMqIntegrationEventPublisher>();

                services.AddHostedService<
                    RabbitMqTopologyInitializer>();
            }
            else
            {
                services.AddSingleton<
                    IIntegrationEventPublisher,
                    InMemoryIntegrationEventPublisher>();
            }

            return services;
        }
    }
}