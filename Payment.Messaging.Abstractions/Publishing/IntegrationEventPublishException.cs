using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Messaging.Abstractions.Publishing
{
    public sealed class IntegrationEventPublishException : Exception
    {
        public PublishFailureKind FailureKind { get; }

        public IntegrationEventPublishException(
            string message,
            PublishFailureKind failureKind,
            Exception? innerException = null)
            : base(message, innerException)
        {
            FailureKind = failureKind;
        }
    }
}
