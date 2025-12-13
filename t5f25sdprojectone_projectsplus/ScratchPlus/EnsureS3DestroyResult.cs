using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus;
using t5f25sdprojectone_projectsplus.ScratchPlus;

public sealed class EnsureS3DestroyResult : IDestroyResult
{
    public bool Destroyed { get; init; }
    public bool NotFound { get; init; }
    public string? BucketName { get; init; }
    public IReadOnlyList<ResourceRecord> RemovedRecords { get; init; } = Array.Empty<ResourceRecord>();
    public string? Message { get; init; }

    // computed convenience
    public int RemovedCount => RemovedRecords?.Count ?? 0;
}
