using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Messaging.Abstractions.Consuming
{
    public interface IIntegrationEventHandler<in TEvent>
       where TEvent : class
    {
        Task HandleAsync(
            TEvent integrationEvent,
            CancellationToken cancellationToken = default);
    }
}
