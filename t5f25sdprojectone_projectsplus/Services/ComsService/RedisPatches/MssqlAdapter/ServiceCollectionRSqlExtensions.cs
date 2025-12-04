// src/Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Convenience extension methods for registering SQL-backed comms components into an
    /// <see cref="IServiceCollection"/>. These helpers wire up options and the default
    /// implementations provided in this assembly.
    /// </summary>
    public static class ServiceCollectionRSqlExtensions
    {
        /// <summary>
        /// Register SQL-backed comms components using the provided options configuration.
        /// Registers <see cref="SqlCommsOptions"/> and the default implementations:
        /// <list type="bullet">
        /// <item><see cref="SqlIdempotencyStore"/> as singleton</item>
        /// <item><see cref="SqlInstanceRegistry"/> as singleton</item>
        /// <item><see cref="SqlBackedConnectionManager"/> as singleton</item>
        /// <item><see cref="SqlMessageEnqueuer"/> as singleton implementing <see cref="IPatchMessageEnqueuer"/></item>
        /// <item><see cref="SqlRemoteForwarder"/> as singleton</item>
        /// </list>
        /// Note: delivery/forward workers require callbacks and are registered via dedicated helpers.
        /// </summary>
        public static IServiceCollection AddSqlRComms(
            this IServiceCollection services,
            Action<SqlCommsOptions>? configure = null) //-------------------------------------------
        {
            configure ??= new Action<SqlCommsOptions>(_ => { });

            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            services.AddSingleton<IRedisClient, MssqlAdapter>();

            // Configure and validate options
            //var configure = services.BuildServiceProvider().GetServices<Action<SqlCommsOptions>>();
            services.Configure(configure);
            services.AddSingleton(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<SqlCommsOptions>>().Value;
                opts.Validate();
                return opts;
            });

            // Register core SQL-backed components as singletons (shared connection string from options)
            services.AddSingleton<SqlIdempotencyStore>(sp =>
            {
                var opts = sp.GetRequiredService<SqlCommsOptions>();
                return new SqlIdempotencyStore(opts.ConnectionString, opts.IdempotencyTable);
            });
            services.AddSingleton<IPatchInstanceRegistry>(sp =>
            {
                var opts = sp.GetRequiredService<SqlCommsOptions>();
                return new SqlInstanceRegistry(opts.ConnectionString, opts.InstancesTable);
            });
            services.AddSingleton<SqlBackedConnectionManager>(sp =>
            {
                var opts = sp.GetRequiredService<SqlCommsOptions>();
                return new SqlBackedConnectionManager(opts.ConnectionString, opts.ConnectionsTable);
            });
            services.AddSingleton<IPatchMessageEnqueuer>(sp =>
            {
                var opts = sp.GetRequiredService<SqlCommsOptions>();
                return new SqlMessageEnqueuer(opts.ConnectionString, opts.MessagesTable);
            });
            services.AddSingleton<SqlRemoteForwarder>(sp =>
            {
                var opts = sp.GetRequiredService<SqlCommsOptions>();
                return new SqlRemoteForwarder(opts.ConnectionString, opts.RemoteForwardsTable);
            });

            return services;
        }

        /// <summary>
        /// Register a <see cref="SqlDeliveryWorker"/> instance that will use the provided delivery callback.
        /// The worker is registered as a singleton and will be constructed with the configured options.
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <param name="deliverCallback">Callback invoked to deliver a message. Must return true on success.</param>
        public static IServiceCollection AddSqlDeliveryWorker(
            this IServiceCollection services,
            Func<PatchMessageEntity, CancellationToken, System.Threading.Tasks.Task<bool>> deliverCallback)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (deliverCallback == null) throw new ArgumentNullException(nameof(deliverCallback));

            services.AddSingleton<SqlDeliveryWorker>(sp =>
            {
                var opts = sp.GetRequiredService<SqlCommsOptions>();
                // Use the messages/deadletters table names from options
                return new SqlDeliveryWorker(
                    opts.ConnectionString,
                    deliverCallback,
                    messagesTable: opts.MessagesTable,
                    deadLettersTable: opts.DeadLettersTable,
                    logger: null);
            });

            return services;
        }

        /// <summary>
        /// Register a <see cref="SqlRemoteForwarder"/> and optionally a processing worker factory.
        /// If you need to process forwards (deliver to remote instances) register a worker separately
        /// using <see cref="AddSqlRemoteForwarderWorker"/>.
        /// </summary>
        public static IServiceCollection AddSqlRemoteForwarder(this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            // SqlRemoteForwarder already registered by AddSqlComms; this is a no-op helper for clarity.
            return services;
        }

        /// <summary>
        /// Register a remote-forward processing worker that uses the provided deliverCallback.
        /// The worker is not a background service; it is a helper object that callers can invoke.
        /// </summary>
        public static IServiceCollection AddSqlRemoteForwarderWorker(
            this IServiceCollection services,
            Func<SqlRemoteForwarder.ForwardRequest, CancellationToken, System.Threading.Tasks.Task<bool>> deliverCallback)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (deliverCallback == null) throw new ArgumentNullException(nameof(deliverCallback));

            services.AddSingleton(sp =>
            {
                var opts = sp.GetRequiredService<SqlCommsOptions>();
                return new SqlRemoteForwarder(opts.ConnectionString, opts.RemoteForwardsTable);
            });

            // Register a small helper wrapper that binds the forwarder and callback together.
            services.AddSingleton<Func<string, System.Threading.Tasks.Task<int>>>(sp =>
            {
                var forwarder = sp.GetRequiredService<SqlRemoteForwarder>();
                return async (targetInstanceId) =>
                {
                    // Process a single batch using options
                    var opts = sp.GetRequiredService<SqlCommsOptions>();
                    var pending = await forwarder.QueryPendingForInstanceAsync(targetInstanceId, opts.MaxForwardBatchSize).ConfigureAwait(false);
                    var processed = 0;
                    foreach (var req in pending)
                    {
                        var ok = await deliverCallback(req, CancellationToken.None).ConfigureAwait(false);
                        if (ok)
                        {
                            await forwarder.RemoveForwardAsync(req.Id).ConfigureAwait(false);
                        }
                        else
                        {
                            // increment attempts / mark pending handled by forwarder consumer if desired
                        }
                        processed++;
                    }
                    return processed;
                };
            });

            return services;
        }

        /// <summary>
        /// Register the SQL idempotency store explicitly (if not using AddSqlComms).
        /// </summary>
        public static IServiceCollection AddSqlIdempotencyStore(this IServiceCollection services, string connectionString, string tableName = "IdempotencyMarkers")
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));

            services.AddSingleton<SqlIdempotencyStore>(_ => new SqlIdempotencyStore(connectionString, tableName));
            services.AddSingleton<IIdempotencyStore>(sp => sp.GetRequiredService<SqlIdempotencyStore>());

            return services;
        }
    }
}
