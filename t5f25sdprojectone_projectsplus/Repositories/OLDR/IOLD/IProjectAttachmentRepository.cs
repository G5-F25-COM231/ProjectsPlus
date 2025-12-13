// Repositories/IProjectAttachmentRepository.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.OLD.Project;

namespace t5f25sdprojectone_projectsplus.Repositories.OLDR.IOLD
{
    public interface IProjectAttachmentRepository
    {
        Task<ProjectAttachment?> GetByIdAsync(long id, CancellationToken ct = default);
        Task<IReadOnlyList<ProjectAttachment>> ListByProjectAsync(long projectId, CancellationToken ct = default);
        Task<ProjectAttachment> CreateAsync(ProjectAttachment attachment, CancellationToken ct = default);
        Task DeleteAsync(long id, CancellationToken ct = default); // soft-delete optional
    }
}
