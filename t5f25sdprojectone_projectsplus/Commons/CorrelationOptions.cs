// src/Common/Correlation/CorrelationOptions.cs
using t5f25sdprojectone_projectsplus.Common.Correlation;

namespace t5f25sdprojectone_projectsplus.Commons
{
    public sealed class CorrelationOptions
    {
        public string HeaderName { get; set; } = CorrelationMiddleware.HeaderName;
        public bool RequireForExternal { get; set; } = true;
    }
}
