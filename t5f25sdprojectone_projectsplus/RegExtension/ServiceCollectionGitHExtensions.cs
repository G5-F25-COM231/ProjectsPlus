// src/ProjectsPlus.GitHub/DependencyInjection/ServiceCollectionExtensions.cs
// Purpose: Register ProjectsPlus GitHub services, HTTP factory, creds providers, webhook handler, and options.
// Usage: services.AddProjectsPlusGitHub(Configuration, envIsProduction: true);

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using t5f25sdprojectone_projectsplus.ScratchPlus;
using t5f25sdprojectone_projectsplus.Services.GithubService;

namespace t5f25sdprojectone_projectsplus.RegExtension
{
    public static class ServiceCollectionGitHExtensions
    {
        /// <summary>
        /// Register ProjectsPlus GitHub services.
        /// - Binds GithubCredsPlus from configuration section "GithubCredsPlus" (for local/dev).
        /// - Registers IGithubCredsProvider (Options-based by default).
        /// - Registers IGitHubHttpClientFactory, GitHubManager (all partials), webhook handler, and related services.
        /// </summary>
        public static IServiceCollection AddProjectsPlusGitHub(this IServiceCollection services, IConfiguration configuration, bool envIsProduction = false)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            // Bind GithubCredsPlus to options for local/dev usage.
            services.Configure<GithubCredsPlus>(configuration.GetSection("GithubCredsPlus"));

            // Register creds provider:
            // - In production, you should replace OptionsGithubCredsProvider with a VaultGithubCredsProvider implementation wired to your secret store.
            services.AddSingleton<IGithubCredsProvider>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<OptionsGithubCredsProvider>>();
                var opts = sp.GetRequiredService<IOptionsMonitor<GithubCredsPlus>>();
                var provider = new OptionsGithubCredsProvider(opts);
                // Validate once at startup to fail fast if misconfigured (only for non-production).
                try
                {
                    var creds = opts.CurrentValue;
                    creds?.ValidateForUse();
                }
                catch (Exception ex)
                {
                    // In production we prefer to not throw here; log and continue so a vault provider can be swapped in.
                    if (envIsProduction)
                    {
                        logger.LogWarning(ex, "GithubCredsPlus options validation failed at startup (production mode). Ensure a vault-backed IGithubCredsProvider is registered.");
                    }
                    else
                    {
                        throw;
                    }
                }
                return provider;
            });

            // Register HTTP factory (singleton)
            services.AddSingleton<IGitHubHttpClientFactory>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<GitHubHttpClientFactory>>();
                // productName can be customized via config if desired
                return new GitHubHttpClientFactory(productName: "ProjectsPlus");
            });

            // Register GitHubManager as a single implementation for all partial interfaces
            services.AddSingleton<GitHubManager>();
            services.AddSingleton<IGitHubRepositoryManager>(sp => sp.GetRequiredService<GitHubManager>());
            services.AddSingleton<IProjectBoardService>(sp => sp.GetRequiredService<GitHubManager>());
            services.AddSingleton<ICollaboratorService>(sp => sp.GetRequiredService<GitHubManager>());
            services.AddSingleton<IContributionService>(sp => sp.GetRequiredService<GitHubManager>());
            services.AddSingleton<IPurgeService>(sp => sp.GetRequiredService<GitHubManager>());

            // Webhook handler
            services.AddSingleton<IGitHubWebhookHandler, GitHubWebhookHandler>();

            // Optional: register controller dependencies (if using ASP.NET Core controllers)
            services.AddControllers().AddApplicationPart(typeof(GitHubWebhookController).Assembly);

            return services;
        }

        /// <summary>
        /// Helper to replace the default Options-based creds provider with a vault-backed provider.
        /// Call this in Startup/Program when you have a vault fetch delegate available.
        /// </summary>
        public static IServiceCollection UseVaultGithubCredsProvider(this IServiceCollection services, Func<CancellationToken, Task<string>> fetchSecretJsonAsync, TimeSpan? cacheTtl = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (fetchSecretJsonAsync == null) throw new ArgumentNullException(nameof(fetchSecretJsonAsync));

            // Remove existing IGithubCredsProvider registration(s) and replace with Vault provider
            services.AddSingleton<IGithubCredsProvider>(sp => new VaultGithubCredsProvider(fetchSecretJsonAsync, cacheTtl));
            return services;
        }
    }
}
