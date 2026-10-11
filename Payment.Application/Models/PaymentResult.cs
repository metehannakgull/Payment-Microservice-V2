using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Application.Models
{
    public class PaymentResult
    {
        public bool IsSuccess { get; set; }

        public string? TransactionId { get; set; }

        public string? ErrorCode { get; set; }

        public string? ErrorMessage { get; set; }

        public string? ThreeDSecureUrl { get; set; }
    }
}
