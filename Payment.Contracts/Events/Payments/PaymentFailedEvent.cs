using Payment.Contracts.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Contracts.Events.Payments
{
    public sealed record PaymentFailedEvent(
       Guid PaymentId,
       string OrderId,
       decimal Amount,
       string Currency,
       string Provider,
       string? ErrorCode,
       string? ErrorMessage,
       DateTime FailedAt)
       : IntegrationEvent;
}
