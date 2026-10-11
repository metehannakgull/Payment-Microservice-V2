using Microsoft.Extensions.DependencyInjection;
using Payment.Application.Features.Payments.CreatePayment;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Application.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplication(
            this IServiceCollection services)
        {
            services.AddScoped<CreatePaymentService>();

            return services;
        }
    }
}
