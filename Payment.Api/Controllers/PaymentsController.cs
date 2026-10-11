using Microsoft.AspNetCore.Mvc;
using Payment.Application.Abstractions.Idempotency;
using Payment.Application.Features.Payments.CreatePayment;

namespace Payment.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentsController : ControllerBase
    {
        private readonly CreatePaymentService _createPaymentService;

        public PaymentsController(
            CreatePaymentService createPaymentService)
        {
            _createPaymentService = createPaymentService;
        }

        [HttpPost]
        public async Task<IActionResult> CreatePayment(
            [FromHeader(Name = "Idempotency-Key")]
            string? idempotencyKey,

            [FromBody]
            CreatePaymentCommand command,

            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return BadRequest(new
                {
                    message =
                        "Idempotency-Key header is required."
                });
            }

            command.IdempotencyKey =
                idempotencyKey;

            var result =
                await _createPaymentService.ExecuteAsync(
                    command,
                    cancellationToken);

            return Ok(result);
        }
    }
}