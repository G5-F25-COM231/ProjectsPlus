// src/Infrastructure/Health/CommsHealthChecks.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.ECS.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// Composite health check for the comms stack.
    /// - Verifies DynamoDB table is reachable (DescribeTable)
    /// - Verifies local instance heartbeat (via IInstanceRegistry) is recent enough
    /// 
    /// This single IHealthCheck can be registered with the health checks system.
    /// </summary>
    public sealed class CommsHealthChecks : IHealthCheck
    {
        private readonly IAmazonDynamoDB _ddb;
        private readonly IInstanceRegistry _registry;
        private readonly CommsOptions _options;
        private readonly ILogger<CommsHealthChecks> _logger;

        private readonly IServiceProvider _services;

        /// <summary>
        /// How old the instance heartbeat may be before we consider the worker degraded.
        /// Default: 60 seconds.
        /// </summary>
        public TimeSpan InstanceStaleThreshold { get; set; } = TimeSpan.FromSeconds(60);

        public CommsHealthChecks(
            IServiceProvider services//,
            //IAmazonDynamoDB ddb,
            //IInstanceRegistry registry,
            //IOptions<CommsOptions> options,
            //ILogger<CommsHealthChecks> logger
            )
        {

            _services = services;
            using var scope = _services.CreateScope();
            var reg = scope.ServiceProvider.GetRequiredService<IInstanceRegistry>();
            var ddb = scope.ServiceProvider.GetRequiredService<DynamodbService>();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<CommsOptions>>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<CommsHealthChecks>>();

            _ddb = ddb.DdbClient ?? throw new ArgumentNullException(nameof(ddb));
            _registry = reg ?? throw new ArgumentNullException(nameof(reg));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            // 1) DynamoDB table connectivity
            if (string.IsNullOrWhiteSpace(_options.TableName))
            {
                _logger.LogWarning("Comms health check misconfigured: Comms:TableName is not set");
                return HealthCheckResult.Unhealthy("Comms:TableName is not configured");
            }

            try
            {
                // Use DescribeTable to validate table exists and is reachable
                var desc = await _ddb.DescribeTableAsync(new DescribeTableRequest { TableName = _options.TableName }, cancellationToken).ConfigureAwait(false);
                if (desc?.Table == null)
                {
                    _logger.LogWarning("Comms health: DescribeTable returned no table for {Table}", _options.TableName);
                    return HealthCheckResult.Unhealthy($"DynamoDB table {_options.TableName} not found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Comms health: DynamoDB DescribeTable failed for {Table}", _options.TableName);
                return HealthCheckResult.Unhealthy($"DynamoDB unreachable or table {_options.TableName} not accessible: {ex.Message}");
            }

            // 2) Instance/worker liveness (optional if InstanceId configured)
            var instanceId = _options.InstanceId;
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                // If no instance id configured, we consider Dynamo check sufficient for healthy
                return HealthCheckResult.Healthy("DynamoDB reachable; instance id not configured");
            }

            try
            {
                var inst = await _registry.GetInstanceAsync(instanceId, cancellationToken).ConfigureAwait(false);
                if (inst == null)
                {
                    _logger.LogWarning("Comms health: instance {InstanceId} not registered", instanceId);
                    return HealthCheckResult.Unhealthy($"Instance {instanceId} not registered");
                }

                var age = DateTime.UtcNow - inst.LastHeartbeatUtc;
                if (age <= InstanceStaleThreshold)
                {
                    return HealthCheckResult.Healthy($"DynamoDB reachable; instance {instanceId} heartbeat OK (age {age.TotalSeconds:F0}s)");
                }
                else
                {
                    _logger.LogWarning("Comms health: instance {InstanceId} heartbeat stale ({Age}s)", instanceId, age.TotalSeconds);
                    return HealthCheckResult.Degraded($"Instance {instanceId} heartbeat stale ({age.TotalSeconds:F0}s)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Comms health: error checking instance heartbeat for {InstanceId}", instanceId);
                return HealthCheckResult.Unhealthy($"Error checking instance heartbeat: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Minimal options used by the comms stack health checks and wiring.
    /// Keep this class small and focused so it can be shared with ServiceCollectionExtensions.
    /// </summary>
    //public sealed class CommsOptions
    //{
    //    /// <summary>
    //    /// DynamoDB table name used by the comms adapter.
    //    /// </summary>
    //    public string? TableName { get; set; }

    //    /// <summary>
    //    /// Local instance id (used for registry/worker identity).
    //    /// </summary>
    //    public string? InstanceId { get; set; }

    //    /// <summary>
    //    /// Delivery worker page size (optional).
    //    /// </summary>
    //    public int DeliveryPageSize { get; set; } = 25;

    //    /// <summary>
    //    /// Delivery worker poll interval in milliseconds (optional).
    //    /// </summary>
    //    public int DeliveryPollMs { get; set; } = 500;
    //}
}
