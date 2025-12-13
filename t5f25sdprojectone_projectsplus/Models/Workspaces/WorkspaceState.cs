namespace t5f25sdprojectone_projectsplus.Models.Workspaces
{
    public enum WorkspaceState
    {
        ProvisioningPending = 0,
        ProvisioningInProgress = 10,
        Provisioned = 20,
        PartialProvisioned = 30,
        ProvisioningFailed = 40,
        Active = 50,
        Archived = 60
    }
}
