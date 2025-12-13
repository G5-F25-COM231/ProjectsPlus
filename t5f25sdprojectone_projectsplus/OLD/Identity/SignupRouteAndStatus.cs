// Models/Identity/SignupRouteAndStatus.cs
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace t5f25sdprojectone_projectsplus.OLD.Identity
{
    /// <summary>
    /// SignupRoute enumerates the origin of a user account creation.
    /// Keep values stable because migrations, backfill jobs and business logic may persist or branch on these names.
    /// </summary>
    public enum SignupRoute
    {
        /// <summary>
        /// Standard self-service signup (user created themselves via public registration).
        /// </summary>
        SelfService = 0,

        /// <summary>
        /// Admin account created during initial seeded bootstrap (created by operators/runbook).
        /// </summary>
        AdminSeed = 10,

        /// <summary>
        /// Account created by an existing admin (admin-created secondary admin or user).
        /// </summary>
        AdminCreated = 20,

        /// <summary>
        /// External onboarding route (applicant supplies JoiningPurpose and supporting documents;
        /// requires admin approval before activation unless policy overrides).
        /// </summary>
        External = 30
    }

    /// <summary>
    /// SignupStatus represents lifecycle state of the signup process.
    /// Values are intentionally explicit and sparse to allow adding intermediate states later.
    /// </summary>
    public enum SignupStatus
    {
        /// <summary>
        /// Active and usable account (default for existing migrated users).
        /// </summary>
        Active = 0,

        /// <summary>
        /// The account is pending explicit admin approval (used for External).
        /// </summary>
        PendingApproval = 10,

        /// <summary>
        /// Admin declined the signup request; justification stored in audit.
        /// </summary>
        Declined = 20,

        /// <summary>
        /// Signup approved but not yet completed first-login tasks (e.g., forced password change).
        /// </summary>
        Approved = 30,

        /// <summary>
        /// Account suspended for policy or security reasons.
        /// </summary>
        Suspended = 40
    }
}
