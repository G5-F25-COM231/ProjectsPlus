using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.Interfaces
{
    public interface IAuditLogger
    {
        Task LogAsync(string eventType, string message, CancellationToken ct = default);
    }
}
