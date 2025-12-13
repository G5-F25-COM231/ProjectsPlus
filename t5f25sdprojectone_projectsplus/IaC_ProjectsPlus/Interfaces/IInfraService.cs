using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.Interfaces
{
    public interface IInfraService
    {
        /// <summary>Run the full create orchestration and return the summary.</summary>
        Task<InfraOrchestratorSummary> CreateAllAsync(InfraCreateRequest req, CancellationToken ct = default);

        /// <summary>Run the full destroy orchestration and return the summary.</summary>
        Task<InfraOrchestratorSummary> DeleteAllAsync(InfraDestroyRequest req, CancellationToken ct = default);

        /// <summary>Run existence checks for the supplied identifiers and return the summary.</summary>
        Task<InfraOrchestratorExistsSummary> ExistsAsync(InfraExistsRequest req, CancellationToken ct = default);

        /// <summary>Read-only snapshot of the last successful infrastructure map produced by CreateAllAsync.</summary>
        IReadOnlyDictionary<string, object> InfrastructureSnapshot { get; }

        /// <summary>Clear the in-memory snapshot (does not affect cloud resources).</summary>
        void ClearSnapshot();
    }
}
