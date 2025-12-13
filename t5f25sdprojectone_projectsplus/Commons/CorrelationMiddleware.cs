using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace t5f25sdprojectone_projectsplus.Common.Correlation
{
    public class CorrelationMiddleware
    {
        public const string HeaderName = "X-Correlation-Id";
        private readonly RequestDelegate _next;

        public CorrelationMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            // Prefer incoming header, otherwise generate
            var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var v) && !string.IsNullOrWhiteSpace(v)
                ? v.ToString()
                : Guid.NewGuid().ToString();

            // Expose to HttpContext.Items for other components
            context.Items[HeaderName] = correlationId;

            // Ensure response contains correlationId
            context.Response.OnStarting(() =>
            {
                if (!context.Response.Headers.ContainsKey(HeaderName))
                    context.Response.Headers.Append(HeaderName, correlationId);
                return Task.CompletedTask;
            });

            await _next(context);
        }
    }
}
