using System;
using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;
using t5f25sdprojectone_projectsplus.Common.Correlation;

namespace t5f25sdprojectone_projectsplus.Commons
{
    public class CorrelationEnricher : ILogEventEnricher
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private const string PropName = "CorrelationId";

        public CorrelationEnricher(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            try
            {
                var ctx = _httpContextAccessor?.HttpContext;
                string? correlation = null;

                if (ctx != null)
                {
                    if (ctx.Items.TryGetValue(CorrelationMiddleware.HeaderName, out var cidObj))
                        correlation = cidObj?.ToString();

                    if (string.IsNullOrEmpty(correlation) && ctx.Request.Headers.TryGetValue(CorrelationMiddleware.HeaderName, out var headerVal))
                        correlation = headerVal.ToString();
                }

                var prop = propertyFactory.CreateProperty(PropName, correlation ?? "none");
                logEvent.AddPropertyIfAbsent(prop);
            }
            catch
            {
                // never fail logging enrichment
            }
        }
    }
}
