// RegExtension/ProjectsPlusServiceCollectionExtensions.cs
using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Users;
using t5f25sdprojectone_projectsplus.Repositories;
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

            // Repositories (depend on ProjectsPlusDbContext)
            services.AddScoped<IAuditRepository, AuditRepository>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IProjectRepository, ProjectRepository>();
            services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();

            // Application services
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IProjectService, ProjectService>();
            services.AddScoped<IWorkspaceService, WorkspaceService>();

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
    }
}
