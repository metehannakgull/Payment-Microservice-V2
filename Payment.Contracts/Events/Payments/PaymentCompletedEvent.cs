using Payment.Contracts.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Contracts.Events.Payments
{
    public sealed record PaymentCompletedEvent(
      Guid PaymentId,
      string OrderId,
      decimal Amount,
      string Currency,
      string Provider,
      string? TransactionId,
      DateTime CompletedAt)
      : IntegrationEvent;
}
