// RegExtension/ProjectsPlusServiceCollectionExtensions.cs
using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using t5f25sdprojectone_projectsplus.Common.Correlation;
using t5f25sdprojectone_projectsplus.Commons;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Users;
using t5f25sdprojectone_projectsplus.Repositories;
using t5f25sdprojectone_projectsplus.Repositories.EF;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
using t5f25sdprojectone_projectsplus.Services;
using t5f25sdprojectone_projectsplus.Services.Authorization;
using t5f25sdprojectone_projectsplus.Services.Authorization.Interfaces;
using t5f25sdprojectone_projectsplus.Services.Interfaces;

namespace t5f25sdprojectone_projectsplus.RegExtension
{
    /// <summary>
    /// Centralized service registration for ProjectsPlus.
    /// - Keep AddProjectsPlus minimal: assumes DbContext already registered by the host.
    /// - AddProjectsPlusWithDb registers the DbContext and then the same surface.
    /// - AddProjectsPlusWithInMemoryDb is a convenience for tests/samples.
    /// 
    /// Note: Authorization is feature-flagged. Pass enablePolicyAuth=true to opt into Phase 5
    /// policy-backed authorization (requires IAuthorizationRepository to be available and may create
    /// additional tables via migrations). Default keeps the permissive stub to avoid breaking existing hosts.
    /// </summary>
    public static class ProjectsPlusServiceCollectionExtensions
    {
        /// <summary>
        /// Registers ProjectsPlus services assuming the consumer has already registered ProjectsPlusDbContext.
        /// Call this from Program.cs after AddDbContext / AddDbContextFactory.
        /// </summary>
        /// <param name="services">IServiceCollection to register into</param>
        /// <param name="enablePolicyAuth">
        /// When true: registers the repository-backed PolicyAuthorizationService, memory cache, the authorization
        /// repository, and a hosting initializer to seed roles/permissions (idempotent). When false: keeps existing
        /// StubAuthorizationService for backward compatibility.
        /// </param>
        /// <param name="configureAuthorizationOptions">Optional callback to customize AuthorizationOptions when policy auth is enabled</param>
        public static IServiceCollection AddProjectsPlus(
            this IServiceCollection services,
            bool enablePolicyAuth = false, // true
            Action<AuthorizationOptions>? configureAuthorizationOptions = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            // ---------------------------------------------------------
            // Minimal common infra
            // ---------------------------------------------------------
            services.AddHttpContextAccessor();

            // Correlation options used by controllers and middleware
            services.AddSingleton(new CorrelationOptions { HeaderName = CorrelationMiddleware.HeaderName, RequireForExternal = true });

            // Serilog enricher registration (optional; Serilog can resolve this from DI when configured)
            services.AddSingleton<ILogEventEnricher, CorrelationEnricher>();

            // ---------------------------------------------------------
            // Repositories (depend on ProjectsPlusDbContext)
            // Keep registrations pointing at EF implementations.
            // If you later add alternative implementations (e.g., for tests),
            // replace these registrations at composition root.
            // ---------------------------------------------------------
            services.AddScoped<IAuditRepository, AuditRepository>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IProjectRepository, ProjectRepository>();
            services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
            services.AddScoped<IProjectStateChangeRepository, ProjectStateChangeRepository>();
            services.AddScoped<IResourceRecordRepository, ResourceRecordRepository>();

            // Authorization repository (Phase 5). Register unconditionally so initializers/tests can resolve it;
            // the service implementation choice (stub vs policy) is feature-flagged below.
            services.AddScoped<IAuthorizationRepository, AuthorizationRepository>();

            // ---------------------------------------------------------
            // Application services
            // ---------------------------------------------------------
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IProjectService, ProjectService>();
            services.AddScoped<IWorkspaceService, WorkspaceService>();

            // Infra & orchestration stubs for Phase 4
            services.AddScoped<IInfraOrchestrator, StubInfraOrchestrator>();

            // Password hashing
            services.AddScoped<IPasswordHasher<UserEntity>, PasswordHasher<UserEntity>>();

            // ---------------------------------------------------------
            // Authorization wiring (feature flag)
            // - keep stub by default to avoid schema or behavioral changes
            // - if enabled, register memory cache, options, policy service and initializer
            // ---------------------------------------------------------
            if (enablePolicyAuth)
            {
                // Memory cache used by PolicyAuthorizationService for read-through caching of permissions
                services.AddMemoryCache();

                // Bind default options and allow caller to override via callback
                var defaultOptions = new AuthorizationOptions();
                configureAuthorizationOptions?.Invoke(defaultOptions);
                services.Configure<AuthorizationOptions>(opts =>
                {
                    opts.AdminRoleName = defaultOptions.AdminRoleName;
                    opts.CacheDurationSeconds = defaultOptions.CacheDurationSeconds;
                });

                // Repository-backed policy service
                services.AddScoped<Services.Authorization.Interfaces.IAuthorizationService, PolicyAuthorizationService>();

                // Background initializer that seeds minimal roles/permissions in an idempotent way.
                // Registered as hosted service so it runs during application startup.
                services.AddHostedService<AuthorizationInitializer>();
            }
            else
            {
                // Backwards-compatible permissive stub service for Phase 4
                services.AddScoped<Services.Authorization.Interfaces.IAuthorizationService, StubAuthorizationService>();
            }

            return services;
        }

        /// <summary>
        /// Convenience overload that also registers ProjectsPlusDbContext using the provided DbContext options action.
        /// Useful for small apps or tests where you want one call to configure the database provider (SQLite, InMemory, etc.)
        /// Example:
        /// services.AddProjectsPlusWithDb(ctx => ctx.UseSqlite(connection), enablePolicyAuth: true);
        /// </summary>
        public static IServiceCollection AddProjectsPlusWithDb(
            this IServiceCollection services,
            Action<DbContextOptionsBuilder> configureDb,
            bool enablePolicyAuth = false,
            Action<AuthorizationOptions>? configureAuthorizationOptions = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configureDb == null) throw new ArgumentNullException(nameof(configureDb));

            // Register the DbContext first so repositories can be resolved
            services.AddDbContext<ProjectsPlusDbContext>(configureDb);

            // Now register the rest (pass through the auth options)
            services.AddProjectsPlus(enablePolicyAuth, configureAuthorizationOptions);

            return services;
        }

        /// <summary>
        /// Convenience helper to register ProjectsPlus and an in-memory EF Core provider.
        /// Useful for tests and simple sample apps. The in-memory database name can be provided to isolate tests.
        /// Example:
        /// services.AddProjectsPlusWithInMemoryDb("test-db", enablePolicyAuth: true);
        /// </summary>
        public static IServiceCollection AddProjectsPlusWithInMemoryDb(
            this IServiceCollection services,
            string inMemoryDatabaseName = "ProjectsPlus_InMemory",
            bool enablePolicyAuth = false, // true
            Action<AuthorizationOptions>? configureAuthorizationOptions = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (string.IsNullOrWhiteSpace(inMemoryDatabaseName)) throw new ArgumentException("Database name must be provided", nameof(inMemoryDatabaseName));

            services.AddDbContext<ProjectsPlusDbContext>(opts => opts.UseInMemoryDatabase(inMemoryDatabaseName));
            services.AddProjectsPlus(enablePolicyAuth, configureAuthorizationOptions);

            return services;
        }
    }
}
