// src/Services/LocalStubInfraOrchestrator.cs
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Services.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services
{
    public class StubInfraOrchestrator : IInfraOrchestrator
    {
        public Task<InfraPreflightReport> DryRunAsync(long projectId, CancellationToken ct = default)
        {
            return Task.FromResult(new InfraPreflightReport { CanExecute = true, Summary = "stub preflight ok" });
        }

        public Task<InfraExecutionResult> ExecuteAsync(long projectId, CancellationToken ct = default)
        {
            return Task.FromResult(new InfraExecutionResult { Success = true, Ticket = $"stub-{projectId}-{System.Guid.NewGuid()}" });
        }
    }
}
