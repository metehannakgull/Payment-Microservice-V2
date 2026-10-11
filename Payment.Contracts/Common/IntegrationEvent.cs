using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Contracts.Common
{
    public abstract record IntegrationEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();

        public string CorrelationId { get; init; } = string.Empty;

        public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    }
}
