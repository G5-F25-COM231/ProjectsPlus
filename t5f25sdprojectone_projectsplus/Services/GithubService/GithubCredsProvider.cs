// src/ProjectsPlus.GitHub/Creds/GithubCredsProvider.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace t5f25sdprojectone_projectsplus.Services.GithubService
{
    /// <summary>
    /// Abstraction to obtain GithubCredsPlus securely.
    /// Implementations should read from a secrets vault in production.
    /// </summary>
    public interface IGithubCredsProvider
    {
        /// <summary>
        /// Return the current GithubCredsPlus instance. Caller must not modify returned object.
        /// </summary>
        Task<GithubCredsPlus> GetCredsAsync(CancellationToken ct = default);

        /// <summary>
        /// Reload credentials from backing store (if supported).
        /// </summary>
        Task ReloadAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Simple in-memory provider for local development and tests.
    /// Use VaultGithubCredsProvider in production.
    /// </summary>
    public sealed class InMemoryGithubCredsProvider : IGithubCredsProvider
    {
        private GithubCredsPlus _creds;

        public InMemoryGithubCredsProvider(GithubCredsPlus creds)
        {
            _creds = creds ?? throw new ArgumentNullException(nameof(creds));
            _creds.ValidateForUse();
        }

        public Task<GithubCredsPlus> GetCredsAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_creds);
        }

        public Task ReloadAsync(CancellationToken ct = default)
        {
            // No-op for in-memory provider
            return Task.CompletedTask;
        }

        /// <summary>
        /// Replace credentials at runtime (useful for tests).
        /// </summary>
        public void Replace(GithubCredsPlus newCreds)
        {
            if (newCreds == null) throw new ArgumentNullException(nameof(newCreds));
            newCreds.ValidateForUse();
            _creds = newCreds;
        }
    }

    /// <summary>
    /// Vault-backed provider skeleton. Implementors should wire to your secret store (KeyVault, AWS Secrets Manager, etc).
    /// This class intentionally keeps implementation details out of the contract; fill in the secret retrieval logic.
    /// </summary>
    public sealed class VaultGithubCredsProvider : IGithubCredsProvider
    {
        private readonly Func<CancellationToken, Task<string>> _fetchSecretJsonAsync;
        private GithubCredsPlus? _cached;
        private readonly TimeSpan _cacheTtl;
        private DateTime _cacheExpiryUtc;

        /// <summary>
        /// Provide a delegate that returns the secret JSON payload from your vault.
        /// </summary>
        public VaultGithubCredsProvider(Func<CancellationToken, Task<string>> fetchSecretJsonAsync, TimeSpan? cacheTtl = null)
        {
            _fetchSecretJsonAsync = fetchSecretJsonAsync ?? throw new ArgumentNullException(nameof(fetchSecretJsonAsync));
            _cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(5);
            _cacheExpiryUtc = DateTime.MinValue;
        }

        public async Task<GithubCredsPlus> GetCredsAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (_cached != null && DateTime.UtcNow < _cacheExpiryUtc)
            {
                return _cached;
            }

            var json = await _fetchSecretJsonAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("Vault returned empty GitHub credentials payload.");

            var creds = System.Text.Json.JsonSerializer.Deserialize<GithubCredsPlus>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? throw new InvalidOperationException("Failed to deserialize GithubCredsPlus from vault payload.");

            creds.ValidateForUse();
            _cached = creds;
            _cacheExpiryUtc = DateTime.UtcNow.Add(_cacheTtl);
            return _cached;
        }

        public Task ReloadAsync(CancellationToken ct = default)
        {
            _cached = null;
            _cacheExpiryUtc = DateTime.MinValue;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Small helper to bind GithubCredsPlus from IOptions when using appsettings for local dev.
    /// </summary>
    public sealed class OptionsGithubCredsProvider : IGithubCredsProvider
    {
        private readonly IOptionsMonitor<GithubCredsPlus> _options;

        public OptionsGithubCredsProvider(IOptionsMonitor<GithubCredsPlus> options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public Task<GithubCredsPlus> GetCredsAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var creds = _options.CurrentValue ?? throw new InvalidOperationException("GithubCredsPlus not configured in options.");
            creds.ValidateForUse();
            return Task.FromResult(creds);
        }

        public Task ReloadAsync(CancellationToken ct = default)
        {
            // IOptionsMonitor updates automatically; nothing to do.
            return Task.CompletedTask;
        }
    }
}
