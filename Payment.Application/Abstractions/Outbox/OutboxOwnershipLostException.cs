using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Application.Abstractions.Outbox
{
    public sealed class OutboxOwnershipLostException
        : Exception
    {
        public Guid MessageId { get; }

        public string LockId { get; }

        public OutboxOwnershipLostException(
            Guid messageId,
            string lockId,
            string? message = null)
            : base(
                message ??
                $"Outbox ownership was lost. MessageId={messageId}, LockId={lockId}.")
        {
            MessageId = messageId;
            LockId = lockId;
        }
    }
}
