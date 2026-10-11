using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Messaging.Abstractions.Consuming
{
    public enum ProcessedMessageReservationResult
    {
        Acquired = 0,
        AlreadyProcessed = 1,
        InProgress = 2
    }
}
