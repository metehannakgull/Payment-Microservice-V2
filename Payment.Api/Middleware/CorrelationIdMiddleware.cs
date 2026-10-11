namespace Payment.Api.Middleware
{
    public sealed class CorrelationIdMiddleware
    {
        private const string HeaderName =
            "X-Correlation-ID";

        private const string ItemName =
            "CorrelationId";

        private readonly RequestDelegate _next;

        public CorrelationIdMiddleware(
            RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ILogger<CorrelationIdMiddleware> logger)
        {
            var correlationId =
                context.Request.Headers
                    .TryGetValue(HeaderName, out var headerValue)
                &&
                !string.IsNullOrWhiteSpace(
                    headerValue.ToString())
                    ? headerValue.ToString()
                    : Guid.NewGuid().ToString("N");

            context.Items[ItemName] =
                correlationId;

            context.Response.Headers[HeaderName] =
                correlationId;

            using (logger.BeginScope(
                new Dictionary<string, object>
                {
                    ["CorrelationId"] =
                        correlationId
                }))
            {
                await _next(context);
            }
        }
    }
}
