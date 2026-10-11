using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.NotificationWorker.Messaging.RabbitMq
{
    public sealed class RabbitMqOptions
    {
        public string HostName { get; set; } = "127.0.0.1";

        public int Port { get; set; } = 5673;

        public string UserName { get; set; } = "guest";

        public string Password { get; set; } = "guest";

        public string VirtualHost { get; set; } = "/";


        // MAIN EXCHANGE / QUEUE
        public string ExchangeName { get; set; }
            = "payment.events";

        public string QueueName { get; set; }
            = "notification.payment.completed";

        public string RoutingKey { get; set; }
            = "payment.completed";


        // RETRY
        public string RetryExchangeName { get; set; }
            = "notification.retry";

        public string RetryQueueName { get; set; }
            = "notification.payment.completed.retry";

        public string RetryRoutingKey { get; set; }
            = "notification.payment.completed.retry";

        public int RetryDelayMilliseconds { get; set; }
            = 5000;

        public int MaxRetryCount { get; set; }
            = 3;


        // DEAD LETTER
        public string DeadLetterExchangeName { get; set; }
            = "notification.dlx";

        public string DeadLetterQueueName { get; set; }
            = "notification.payment.completed.dlq";

        public string DeadLetterRoutingKey { get; set; }
            = "notification.payment.completed.dead";
    }
}
