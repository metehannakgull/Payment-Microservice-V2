using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Application.Abstractions.Observability
{
    public interface ICorrelationContext
    {
        string CorrelationId { get; }
    }
}
