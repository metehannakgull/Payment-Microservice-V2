using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Infrastructure.Messaging.RabbitMq
{
    public sealed class RabbitMqOptions
    {
        public const string SectionName = "Messaging:RabbitMq";

        public string HostName { get; set; } = "localhost";

        public int Port { get; set; } = 5672;

        public string UserName { get; set; } = "guest";

        public string Password { get; set; } = "guest";

        public string VirtualHost { get; set; } = "/";

        public string ExchangeName { get; set; } = "payment.events";
    }
}
