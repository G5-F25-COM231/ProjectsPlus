// RegExtension/ProjectsPlusServiceCollectionExtensions.cs
using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
using t5f25sdprojectone_projectsplus.Services.Interfaces;

namespace t5f25sdprojectone_projectsplus.RegExtension
{
    public static class ProjectsPlusServiceCollectionExtensions
    {
        /// <summary>
        /// Registers ProjectsPlus services assuming the consumer has already registered ProjectsPlusDbContext.
        /// This keeps the surface minimal when the host configures DbContext separately (recommended for apps/tests).
        /// Call this from Program.cs after AddDbContext / AddDbContextFactory.
        /// </summary>
        public static IServiceCollection AddProjectsPlus(this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            // Ensure small common infra
            services.AddHttpContextAccessor();

            // Correlation options used by controllers and middleware
            services.AddSingleton(new CorrelationOptions { HeaderName = CorrelationMiddleware.HeaderName, RequireForExternal = true });

            // Serilog enricher registration (optional; Serilog config can resolve from DI)
            services.AddSingleton<ILogEventEnricher, CorrelationEnricher>();

            // Repositories (depend on ProjectsPlusDbContext)
            services.AddScoped<IAuditRepository, AuditRepository>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IProjectRepository, ProjectRepository>();
            services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
            services.AddScoped<IProjectStateChangeRepository, ProjectStateChangeRepository>();
            services.AddScoped<IResourceRecordRepository, ResourceRecordRepository>();

            // Application services
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IProjectService, ProjectService>();
            services.AddScoped<IWorkspaceService, WorkspaceService>();

            // Infra & orchestration stubs for Phase 4
            services.AddScoped<IInfraOrchestrator, StubInfraOrchestrator>();

            // Authorization (stub for now; Phase 5 will replace)
            services.AddScoped<IAuthorizationService, StubAuthorizationService>();

            // Common infra
            services.AddScoped<IPasswordHasher<UserEntity>, PasswordHasher<UserEntity>>();

            return services;
        }

        /// <summary>
        /// Convenience overload that also registers ProjectsPlusDbContext using the provided DbContext options action.
        /// Useful for small apps or tests where you want one call to configure the database provider (SQLite, InMemory, etc.)
        /// Example:
        /// services.AddProjectsPlusWithDb(ctx => ctx.UseSqlite(connection));
        /// </summary>
        public static IServiceCollection AddProjectsPlusWithDb(this IServiceCollection services, Action<DbContextOptionsBuilder> configureDb)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configureDb == null) throw new ArgumentNullException(nameof(configureDb));

            // Register the DbContext first so repositories can be resolved
            services.AddDbContext<ProjectsPlusDbContext>(configureDb);

            // Now register the rest
            services.AddProjectsPlus();

            return services;
        }

        /// <summary>
        /// Convenience helper to register ProjectsPlus and an in-memory EF Core provider.
        /// Useful for tests and simple sample apps. The in-memory database name can be provided to isolate tests.
        /// Example:
        /// services.AddProjectsPlusWithInMemoryDb("test-db");
        /// </summary>
        public static IServiceCollection AddProjectsPlusWithInMemoryDb(this IServiceCollection services, string inMemoryDatabaseName = "ProjectsPlus_InMemory")
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (string.IsNullOrWhiteSpace(inMemoryDatabaseName)) throw new ArgumentException("Database name must be provided", nameof(inMemoryDatabaseName));

            services.AddDbContext<ProjectsPlusDbContext>(opts => opts.UseInMemoryDatabase(inMemoryDatabaseName));
            services.AddProjectsPlus();

            return services;
        }

    }
}
