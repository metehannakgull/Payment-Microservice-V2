using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Messaging.Abstractions.Publishing
{
    public interface IIntegrationEventPublisher
    {
        Task PublishAsync<TEvent>(
            TEvent integrationEvent,
            CancellationToken cancellationToken = default)
            where TEvent : class;
    }
}
