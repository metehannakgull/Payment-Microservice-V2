using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Domain.Enums.Enum
{
    //Burada kullandığımız yaklaşım Domain Modeling
    public enum PaymentStatus
    {
        Pending = 0,
        Processing = 1,
        Requires3DSecure = 2,
        Successful = 3,
        Failed = 4,
        Cancelled = 5,
        Refunded = 6,
        PartiallyRefunded = 7
    }
}
