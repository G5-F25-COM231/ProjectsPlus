using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace t5f25sdprojectone_projectsplus.Commons
{
    public sealed class AuditContext
    {
        public string CorrelationId { get; }
        public DateTimeOffset StartedAtUtc { get; }

        public AuditContext(string correlationId)
        {
            CorrelationId = correlationId;
            StartedAtUtc = DateTimeOffset.UtcNow;
        }

        public override string ToString() => $"AuditContext:cid={CorrelationId}:t={StartedAtUtc:O}";
    }

    public class AuditMiddleware
    {
        private readonly RequestDelegate _next;
        private const string AuditKey = "AuditContext";

        public AuditMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            // Obtain correlation from Items set by CorrelationMiddleware or from header
            var correlation = context.Items.TryGetValue(Common.Correlation.CorrelationMiddleware.HeaderName, out var cid)
                ? cid?.ToString()
                : context.Request.Headers.TryGetValue(Common.Correlation.CorrelationMiddleware.HeaderName, out var headerVal)
                    ? headerVal.ToString()
                    : Guid.NewGuid().ToString();

            var audit = new AuditContext(correlation);
            context.Items[AuditKey] = audit;

            // the audit context remains available for the lifetime of the request
            await _next(context);
        }

        public static AuditContext? Get(HttpContext context)
        {
            if (context.Items.TryGetValue(AuditKey, out var obj) && obj is AuditContext ac)
                return ac;
            return null;
        }
    }
}
