using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Messaging.Abstractions.Publishing
{
    public enum PublishFailureKind
    {
        Transient = 0,
        Permanent = 1
    }
}
