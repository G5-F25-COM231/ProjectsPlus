// src/Services/IInfraOrchestrator.cs
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.Interfaces
{
    public interface IInfraOrchestration
    {
        /// <summary>
        /// Perform a preflight dry run for the given project id and return a short report.
        /// Implementation can be a stub until Phase 7 where real orchestration is implemented.
        /// </summary>
        Task<InfraPreflightReport> DryRunAsync(CancellationToken ct = default);

        /// <summary>
        /// Execute an orchestration plan for the project. Returns execution ticket or result summary.
        /// </summary>
        Task<InfraExecutionResult> ExecuteAsync(CancellationToken ct = default);
    }

    public sealed class InfraPreflightReport
    {
        public bool CanExecute { get; set; }
        public string Summary { get; set; } = string.Empty;
    }

    public sealed class InfraExecutionResult
    {
        public bool Success { get; set; }
        public string Ticket { get; set; } = string.Empty;
    }
}
