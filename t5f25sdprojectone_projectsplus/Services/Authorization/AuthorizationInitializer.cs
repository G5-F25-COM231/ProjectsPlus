// src/Services/Authorization/AuthorizationInitializer.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using t5f25sdprojectone_projectsplus.Models.Authorization;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services.Authorization
{
    public class AuthorizationInitializer : IHostedService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<AuthorizationInitializer> _logger;
        private readonly AuthorizationOptions _options;

        public AuthorizationInitializer(IServiceProvider services, ILogger<AuthorizationInitializer> logger, IOptions<AuthorizationOptions> options)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? new AuthorizationOptions();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                var scopeFactory = _services.GetService<IServiceScopeFactory>();
                if (scopeFactory == null)
                {
                    _logger.LogWarning("IServiceScopeFactory not available; skipping authorization seed.");
                    return;
                }

                using var scope = scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetService<IAuthorizationRepository>();
                if (repo == null)
                {
                    _logger.LogInformation("IAuthorizationRepository not registered; skipping authorization seed.");
                    return;
                }

                await EnsureSeedAsync(repo, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AuthorizationInitializer failed to seed roles/permissions.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private static async Task EnsureSeedAsync(IAuthorizationRepository repo, CancellationToken ct)
        {
            var perms = new Dictionary<string, string>
            {
                ["Project.View"] = "View project",
                ["Project.Edit"] = "Edit project",
                ["Project.Audit"] = "View project audit"
            };

            foreach (var p in perms)
            {
                var existing = await repo.FindPermissionByNameAsync(p.Key, ct).ConfigureAwait(false);
                if (existing == null)
                {
                    await repo.CreatePermissionAsync(new Permission { Name = p.Key, Description = p.Value }, ct).ConfigureAwait(false);
                }
            }

            // Roles
            var admin = await repo.FindRoleByNameAsync("Admin", ct).ConfigureAwait(false)
                ?? await repo.CreateRoleAsync(new Role { Name = "Admin", Description = "Administrator role" }, ct).ConfigureAwait(false);

            var pm = await repo.FindRoleByNameAsync("ProjectManager", ct).ConfigureAwait(false)
                ?? await repo.CreateRoleAsync(new Role { Name = "ProjectManager", Description = "Project managers" }, ct).ConfigureAwait(false);

            var viewer = await repo.FindRoleByNameAsync("Viewer", ct).ConfigureAwait(false)
                ?? await repo.CreateRoleAsync(new Role { Name = "Viewer", Description = "Read-only users" }, ct).ConfigureAwait(false);

            // Map admin -> perms idempotently
            var viewPerm = await repo.FindPermissionByNameAsync("Project.View", ct).ConfigureAwait(false);
            var editPerm = await repo.FindPermissionByNameAsync("Project.Edit", ct).ConfigureAwait(false);
            var auditPerm = await repo.FindPermissionByNameAsync("Project.Audit", ct).ConfigureAwait(false);

            if (viewPerm != null) await repo.AssignPermissionToRoleAsync(admin.Id, viewPerm.Id, ct).ConfigureAwait(false);
            if (editPerm != null) await repo.AssignPermissionToRoleAsync(admin.Id, editPerm.Id, ct).ConfigureAwait(false);
            if (auditPerm != null) await repo.AssignPermissionToRoleAsync(admin.Id, auditPerm.Id, ct).ConfigureAwait(false);
        }
    }
}
