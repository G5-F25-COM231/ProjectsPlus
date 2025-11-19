namespace t5f25sdprojectone_projectsplus.Models.Projects
{
    public enum ProjectStatus
    {
        Draft = 0,
        Submitted = 10,
        UnderReview = 20,
        ApprovedForEOI = 30,
        PublicForEOI = 35,
        TeamSelection = 40,
        PreparingLaunch = 50,
        LaunchQueued = 60,
        LaunchInProgress = 70,
        Launched = 80,
        Active = 90,
        Completed = 100,
        Archived = 110
    }
}
