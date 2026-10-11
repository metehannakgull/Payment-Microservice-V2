using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Application.Abstractions.Idempotency
{
    public sealed class IdempotencyConflictException : Exception
    {
        public IdempotencyConflictException(string message)
            : base(message)
        {
        }
    }
}
