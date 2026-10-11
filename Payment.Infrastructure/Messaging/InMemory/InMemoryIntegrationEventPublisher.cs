
using Payment.Messaging.Abstractions.Publishing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Infrastructure.Messaging.InMemory
{
    public class InMemoryIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public Task PublishAsync<T>(
            T message,
            CancellationToken cancellationToken = default)
            where T : class
        {
            Console.WriteLine(
                $"[InMemoryEventBus] Event published: {typeof(T).Name}");

            return Task.CompletedTask;
        }
    }
}
