using Payment.Application.Abstractions.Observability;

namespace Payment.Api.Observability
{
    public sealed class HttpCorrelationContext
        : ICorrelationContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpCorrelationContext(
            IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string CorrelationId
        {
            get
            {
                var context =
                    _httpContextAccessor.HttpContext;

                if (context is null)
                    return Guid.NewGuid().ToString("N");

                if (context.Items.TryGetValue(
                    "CorrelationId",
                    out var correlationId))
                {
                    return correlationId?.ToString()
                           ?? Guid.NewGuid().ToString("N");
                }

                return context.TraceIdentifier;
            }
        }
    }
}
