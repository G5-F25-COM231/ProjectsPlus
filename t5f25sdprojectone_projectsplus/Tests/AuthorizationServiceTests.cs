// src/Auth/Tests/AuthorizationServiceTests.cs
using t5f25sdprojectone_projectsplus.Models.Authorization;
using t5f25sdprojectone_projectsplus.Services.Authorization;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.Tests
{
    /// <summary>
    /// Phase 5 acceptance tests:
    /// - Deny-wins: a user that is both allowed by role and explicitly denied by user rule must receive Deny.
    /// - Dual-approval: a PendingAction creation flow that requires two distinct approvers only proceeds when both are authorized.
    /// These tests use the deterministic InMemoryAuthorizationService and an explicit role map for reproducibility.
    /// </summary>
    public class AuthorizationServiceTests
    {
        [Fact]
        public async Task DenyWins_WhenRoleAllows_AndUserDenyExists()
        {
            // Arrange: policy contains an Allow for role "ProjectManager" and a Deny for the specific user id 42
            var policy = new PermissionPolicy
            {
                Rules = new List<PolicyRule>
                {
                    new PolicyRule
                    {
                        Id = "allow-projectmanager",
                        Effect = "Allow",
                        Action = "Project.Edit",
                        Principals = new PrincipalSelector { Roles = new List<string> { "ProjectManager" } },
                        Explain = "Role ProjectManager allowed to edit projects"
                    },
                    new PolicyRule
                    {
                        Id = "deny-user-42",
                        Effect = "Deny",
                        Action = "Project.Edit",
                        Principals = new PrincipalSelector { UserIds = new List<long> { 42 } },
                        Explain = "Explicit deny for user 42"
                    }
                }
            };

            // Role membership: user 42 is in role ProjectManager (so role would allow)
            var roleMap = new Dictionary<long, IEnumerable<string>> { [42] = new[] { "ProjectManager" } };
            var svc = new InMemoryAuthorizationService(policy, roleMap);

            // Act
            var result = await svc.IsAuthorizedAsync(42, "Project.Edit", "Project", 100);

            // Assert: Deny-wins semantics — final result must be Deny
            Assert.False(result.Allowed);
            Assert.Equal("Deny", result.Effect);
            Assert.Contains("deny-user-42", result.Sources);
            Assert.Contains("Deny-wins", result.ExplainText);
        }

        [Fact]
        public async Task DualApproval_PendingActionCreatedOnlyWhenBothApproversAuthorized()
        {
            // Arrange: policy grants Approver role the Approval.Approve action
            var policy = new PermissionPolicy
            {
                Rules = new List<PolicyRule>
                {
                    new PolicyRule
                    {
                        Id = "allow-approver-role",
                        Effect = "Allow",
                        Action = "Approval.Approve",
                        Principals = new PrincipalSelector { Roles = new List<string> { "Approver" } },
                        Explain = "Approver role may approve pending actions"
                    }
                }
            };

            // Role mapping: user 101 and 102 are Approvers; user 103 is not
            var roleMap = new Dictionary<long, IEnumerable<string>>
            {
                [101] = new[] { "Approver" },
                [102] = new[] { "Approver" },
                [103] = Array.Empty<string>()
            };

            var authSvc = new InMemoryAuthorizationService(policy, roleMap);

            // Simple pending-action creation helper used in test
            async Task<bool> TryCreatePendingActionAsync(long actorUserId, long approverA, long approverB)
            {
                // Both approvers must be authorized to approve
                var aRes = await authSvc.IsAuthorizedAsync(approverA, "Approval.Approve");
                if (!aRes.Allowed) return false;

                var bRes = await authSvc.IsAuthorizedAsync(approverB, "Approval.Approve");
                if (!bRes.Allowed) return false;

                // Both authorized => create pending action (return true)
                return true;
            }

            // Both approvers have Approver role => creation succeeds
            var created = await TryCreatePendingActionAsync(actorUserId: 1, approverA: 101, approverB: 102);
            Assert.True(created);

            // One approver not authorized => creation fails
            var notCreated = await TryCreatePendingActionAsync(actorUserId: 1, approverA: 101, approverB: 103);
            Assert.False(notCreated);
        }
    }
}
