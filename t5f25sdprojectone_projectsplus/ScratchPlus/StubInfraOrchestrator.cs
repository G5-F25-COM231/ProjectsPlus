// src/Services/LocalStubInfraOrchestrator.cs
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Services.Interfaces;

namespace t5f25sdprojectone_projectsplus.ScratchPlus
{
    public class StubInfraOrchestrator : IInfraOrchestration
    {
        public Task<InfraPreflightReport> DryRunAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new InfraPreflightReport { CanExecute = true, Summary = "stub preflight ok" });
        }

        public Task<InfraExecutionResult> ExecuteAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new InfraExecutionResult { Success = true, Ticket = $"stub-{Guid.NewGuid()}" });
        }
    }
}
