using Payment.Contracts.Events.Payments;
using Payment.Messaging.Abstractions.Consuming;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.NotificationWorker.Handlers
{
    public sealed class PaymentCompletedEventHandler
        : IIntegrationEventHandler<PaymentCompletedEvent>
    {
        private readonly ILogger<PaymentCompletedEventHandler> _logger;

        public PaymentCompletedEventHandler(
            ILogger<PaymentCompletedEventHandler> logger)
        {
            _logger = logger;
        }

        public async Task HandleAsync(
     PaymentCompletedEvent integrationEvent,
     CancellationToken cancellationToken = default)
        {
            
            _logger.LogInformation(
                "Payment completed. OrderId: {OrderId}, Amount: {Amount} {Currency}, TransactionId: {TransactionId}",
                integrationEvent.OrderId,
                integrationEvent.Amount,
                integrationEvent.Currency,
                integrationEvent.TransactionId);
        }
    }
}
