// src/IaC_ProjectsPlus/IDestroyResult.cs
namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus
{
    public interface IDestroyResult
    {
        bool Destroyed { get; }
        int RemovedCount { get; }
        bool NotFound { get; }
        string? Message { get; }
    }
}
