// src/IaC_ProjectsPlus/InfraService.cs
//
// InfraService
// - Thin application-level façade around IInfraOrchestrator
// - Provides lifecycle methods used by higher-level code (controllers, CLIs, tests)
// - Keeps an in-memory snapshot of the last Infrastructure dictionary produced by successful creates
// - Exposes safe accessors, cancellation-aware operations, and simple logging hooks
// - Includes a DI registration extension for convenience

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.Interfaces;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus
{
  
    public sealed class InfraService : IInfraService
    {
        private readonly IInfraOrchestrator _orchestrator;
        private readonly ILogger<InfraService> _logger;
        private readonly object _snapshotLock = new();
        private Dictionary<string, object> _snapshot = new(StringComparer.OrdinalIgnoreCase);

        public InfraService(IInfraOrchestrator orchestrator, ILogger<InfraService> logger)
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IReadOnlyDictionary<string, object> InfrastructureSnapshot
        {
            get
            {
                lock (_snapshotLock) { return new Dictionary<string, object>(_snapshot, StringComparer.OrdinalIgnoreCase); }
            }
        }

        public void ClearSnapshot()
        {
            lock (_snapshotLock) { _snapshot.Clear(); }
            _logger.LogDebug("[InfraService] Cleared in-memory infrastructure snapshot.");
        }

        public async Task<InfraOrchestratorSummary> CreateAllAsync(InfraCreateRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            _logger.LogInformation("[InfraService] Starting CreateAll orchestration (InvocationId will be in summary).");

            var summary = await _orchestrator.EnsureCreateAllAsync(req, ct).ConfigureAwait(false);

            // If completed and has any steps, attempt to copy orchestrator Infrastructure (if available)
            try
            {
                // If orchestrator exposes Infrastructure, copy it; otherwise, extract from summary steps' results
                var infra = CopyOrchestratorInfrastructureSafe();
                if (infra != null)
                {
                    lock (_snapshotLock) { _snapshot = new Dictionary<string, object>(infra, StringComparer.OrdinalIgnoreCase); }
                    _logger.LogInformation("[InfraService] Infrastructure snapshot updated with {count} entries.", _snapshot.Count);
                }
                else
                {
                    _logger.LogDebug("[InfraService] Orchestrator did not expose Infrastructure; leaving snapshot unchanged.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[InfraService] Failed to capture infrastructure snapshot after create.");
            }

            return summary;
        }

        public async Task<InfraOrchestratorSummary> DeleteAllAsync(InfraDestroyRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            _logger.LogInformation("[InfraService] Starting DeleteAll orchestration.");

            var summary = await _orchestrator.EnsureDeleteAllAsync(req, ct).ConfigureAwait(false);

            // Remove entries that were destroyed from snapshot (best-effort)
            try
            {
                // For each step result, inspect Result for destroyed indicator using helpers
                foreach (var step in summary.Steps ?? new List<StepResult>())
                {
                    var key = MapStepNameToInfrastructureKey(step.StepName);
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    var wasDestroyed = (step.Result as object).WasDestroyed();
                    if (wasDestroyed)
                    {
                        lock (_snapshotLock) { _snapshot.Remove(key); }
                        _logger.LogInformation("[InfraService] Removed {key} from snapshot after destroy.", key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[InfraService] Failed to update snapshot after delete.");
            }

            return summary;
        }

        public async Task<InfraOrchestratorExistsSummary> ExistsAsync(InfraExistsRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            _logger.LogInformation("[InfraService] Running existence checks.");
            var summary = await _orchestrator.EnsureExistsAsync(req, ct).ConfigureAwait(false);
            return summary;
        }

        #region helpers

        // Safe attempt to copy orchestrator's Infrastructure property (if present)
        private IReadOnlyDictionary<string, object>? CopyOrchestratorInfrastructureSafe()
        {
            try
            {
                var infra = _orchestrator.Infrastructure;
                if (infra == null) return null;
                return new Dictionary<string, object>(infra, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return null;
            }
        }

        // Map orchestration step name to the infrastructure snapshot dictionary key used earlier
        private static string? MapStepNameToInfrastructureKey(string stepName)
        {
            if (string.IsNullOrWhiteSpace(stepName)) return null;
            return stepName switch
            {
                "EnsureIAM" or "DestroyIAM" => "IAM",
                "EnsureVPC" or "DestroyVPC" => "VPC",
                "EnsureS3" or "DestroyS3" => "S3",
                "EnsureSM" or "DestroySM" => "SM",
                "EnsureRDS" or "DestroyRDS" => "RDS",
                "EnsureECS" or "DestroyECS" => "ECS.Service", // conservative: service key
                "EnsureDDB" or "DestroyDDB" => "DDB",
                _ => null
            };
        }

        #endregion
    }

}
