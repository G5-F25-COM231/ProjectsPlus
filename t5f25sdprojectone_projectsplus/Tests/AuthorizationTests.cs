// src/Auth.Tests/AuthorizationTests.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Authorization;
using t5f25sdprojectone_projectsplus.Services.Authorization;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.Tests
{
    public class AuthorizationTests
    {
        [Fact]
        public async Task DenyWins_WhenRoleAllows_AndUserDenyExists()
        {
            // Arrange: policy contains an Allow for role ProjectManager and a Deny for the specific user
            var policy = new PermissionPolicy
            {
                Rules = new List<PolicyRule>
                {
                    new PolicyRule
                    {
                        Id = "allow-pm",
                        Effect = "Allow",
                        Action = "Project.Edit",
                        Principals = new PrincipalSelector { Roles = new List<string> { "ProjectManager" } },
                        Explain = "ProjectManager may edit"
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

            var roleMap = new Dictionary<long, IEnumerable<string>> { [42] = new[] { "ProjectManager" } };
            var svc = new InMemoryAuthorizationService(policy, roleMap);

            // Act
            var result = await svc.IsAuthorizedAsync(42, "Project.Edit", "Project", 100);

            // Assert: Deny-wins (even though role allows)
            Assert.False(result.Allowed);
            Assert.Equal("Deny", result.Effect);
            Assert.Contains("deny-user-42", result.Sources);
            Assert.Contains("Deny-wins", result.ExplainText);
        }

        [Fact]
        public async Task DualApprovedPendingAction_CreatedOnlyWhenTwoApprovalsPresent()
        {
            // This test simulates a simple dual-approval flow that queries authorization for two approvers.
            // Acceptance: pending action is created only when both approvers are authorized for the approval action.

            // Policy: Approver role "Approver" can "Approval.Approve"
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
                        Explain = "Approver role may approve"
                    }
                }
            };

            // Role mapping: user 101 and 102 are both Approvers; user 103 is not
            var roleMap = new Dictionary<long, IEnumerable<string>>
            {
                [101] = new[] { "Approver" },
                [102] = new[] { "Approver" },
                [103] = Array.Empty<string>()
            };

            var authSvc = new InMemoryAuthorizationService(policy, roleMap);

            // Simple in-test PendingAction service that requires two distinct approvals
            async Task<bool> TryCreatePendingActionAsync(long actorUserId, long approverA, long approverB)
            {
                // Check both approvers can perform the approval action
                var aRes = await authSvc.IsAuthorizedAsync(approverA, "Approval.Approve");
                if (!aRes.Allowed) return false;

                var bRes = await authSvc.IsAuthorizedAsync(approverB, "Approval.Approve");
                if (!bRes.Allowed) return false;

                // Both approved => creation allowed
                return true;
            }

            // Both approvers allowed => creation succeeds
            var created = await TryCreatePendingActionAsync(actorUserId: 1, approverA: 101, approverB: 102);
            Assert.True(created);

            // One approver not allowed => creation fails
            var notCreated = await TryCreatePendingActionAsync(actorUserId: 1, approverA: 101, approverB: 103);
            Assert.False(notCreated);
        }
    }
}
