// src/IaC_ProjectsPlus/InfraCreateRequestExtensions.cs
using System;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules
{
    public static class InfraCreateReqExtension
    {
        // Default port for RDS when not provided explicitly on InfraCreateRequest.
        // Current orchestrator flow expects a method named RdsPortOrDefault.
        // SQL Server default 1433 is returned. Change if you need engine-specific defaults.
        public static int RdsPortOrDefault(this InfraCreateRequest req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            // If you add a Port property to InfraCreateRequest later, prefer that here.
            return 1433;
        }
    }
}
