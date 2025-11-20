using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Projects;

namespace t5f25sdprojectone_projectsplus.Repositories.Interfaces
{
    public interface IAuditRepository
    {
        Task WriteAsync(ProjectAudit audit, CancellationToken ct = default);
    }
}
