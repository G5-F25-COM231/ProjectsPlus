// src/Infrastructure/Comms/ServiceCollectionExtensions.cs
using Amazon.DynamoDBv2;


namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// DI wiring for the DynamoDB-backed comms adapter.
    /// Registers the minimal, deployable set of services required by the DdbAdapter bundle.
    /// </summary>
    public static class ServiceCollectionRDdbExtensions
    {
        /// <summary>
        /// Adds the DynamoDB comms stack.
        /// Expects configuration section "Comms" to bind to CommsOptions (table name, instance id, etc).
        /// This method registers:
        /// - IAmazonDynamoDB (default client)
        /// - Ddb-backed implementations for enqueue, registry, connections, idempotency
        /// - RemoteForwarder (HttpClient)
        /// - DeliveryWorker as a hosted service (with a simple local-deliver delegate that logs)
        /// </summary>
        public static IServiceCollection AddDdbRComms(this IServiceCollection services, IConfiguration configuration)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            services.AddSingleton<IRedisClient, DdbAdapter>();
            services.AddScoped<ITableNameProvider, TableNameProvider>();

            string tableName;
            IAmazonDynamoDB ddb;
            // Bind options
            services.Configure<CommsOptions>(configuration.GetSection("Comms"));

            // AWS DynamoDB client (uses default credential chain / region)
            services.AddSingleton<IAmazonDynamoDB>(sp =>
            {
                // Create default client; callers may override by registering IAmazonDynamoDB before calling this method.                            
                ddb = sp.GetRequiredService<DynamodbService>().DdbClient;
                return ddb ?? new AmazonDynamoDBClient();
            });

            services.AddSingleton(sp =>
            {
                // Create default client; callers may override by registering IAmazonDynamoDB before calling this method.                          
                using var scope = sp.CreateScope();
                var dydb = scope.ServiceProvider.GetRequiredService<DynamodbService>();
                tableName = dydb.Options.TableName;
                if (string.IsNullOrEmpty(tableName))
                    throw new InvalidOperationException("Missing table name configuration at Comms:Redis:TableName or Redis:TableName.");
                return tableName;
            });


            // Register core DDB-backed services
            services.AddSingleton<IDbMessageEnqueuer, DdbMessageEnqueuer>();
            services.AddSingleton(sp => sp.GetRequiredService<IDbMessageEnqueuer>()); // keep compatibility with IMessageEnqueuer
            services.AddSingleton<IInstanceRegistry, DdbInstanceRegistry>();
            services.AddSingleton<DdbBackedConnectionManager>();
            services.AddSingleton<DdbIdempotencyStore>();

            // Remote forwarder with HttpClient
            services.AddHttpClient<RemoteForwarder>((sp, client) =>
            {
                // HttpClient configuration can be customized via IHttpClientFactory named clients or by the caller.
                client.Timeout = TimeSpan.FromSeconds(10);
            });
            services.AddSingleton<IRemoteForwarder>(sp => sp.GetRequiredService<RemoteForwarder>());

            // Health checks / telemetry wiring can be added by callers; register a minimal health check helper if present
            services.AddSingleton<CommsHealthChecks>();

            // DeliveryWorker requires a deliverLocal delegate. Provide a simple default that logs and completes.
            services.AddSingleton(sp =>
            {
                var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CommsOptions>>().Value;
                var ddb = sp.GetRequiredService<IAmazonDynamoDB>();
                var registry = sp.GetRequiredService<IInstanceRegistry>();
                var connMgr = sp.GetRequiredService<DdbBackedConnectionManager>();
                var forwarder = sp.GetRequiredService<IRemoteForwarder>();
                var logger = sp.GetRequiredService<ILogger<DeliveryWorker>>();

                // Default local delivery delegate: log and complete. Replace by registering DeliveryWorker manually if you need real dispatch.
                Func<string, string, Task> deliverLocalAsync = async (targetId, payload) =>
                {
                    var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Comms.LocalDeliver");
                    log.LogInformation("Default local delivery invoked for target {TargetId}. Payload length: {Len}", targetId, payload?.Length ?? 0);
                    await Task.CompletedTask;
                };

                var instanceId = cfg.InstanceId ?? Guid.NewGuid().ToString();


                return new DeliveryWorker(
                    sp,
                    ddb,
                    cfg.TableName ?? throw new InvalidOperationException("Comms:TableName must be configured"),
                    instanceId,
                    registry,
                    connMgr,
                    forwarder,
                    deliverLocalAsync,
                    logger,
                    pageSize: cfg.DeliveryPageSize,
                    pollDelay: TimeSpan.FromMilliseconds(cfg.DeliveryPollMs)
                );

            });

            // Register DeliveryWorker as a hosted service using the singleton instance above
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<DeliveryWorker>());

            return services;
        }
    }
}
