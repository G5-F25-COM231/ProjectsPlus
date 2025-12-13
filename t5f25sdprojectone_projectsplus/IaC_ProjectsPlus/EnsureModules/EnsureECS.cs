using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.RDS.Model;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules;
using Tag = Amazon.ECS.Model.Tag;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules
{
    public sealed class EnsureECS
    {
        private readonly IAmazonECS _ecs;
        private readonly IAmazonCloudWatchLogs _logs;
        private readonly Infralogger _logger;
        private readonly string _region;

        private const string EnsureIdentifier = "EnsureECS";
        private const string ResourceTypeName = "EcsProfile";

        private EnsureEcsRequest? _lastRequest;
        private EnsureEcsResult? _lastResult;
        private readonly object _stateLock = new();

        public EnsureECS(IAmazonECS ecs, IAmazonCloudWatchLogs logs, Infralogger logger, string region)
        {
            _ecs = ecs ?? throw new ArgumentNullException(nameof(ecs));
            _logs = logs ?? throw new ArgumentNullException(nameof(logs));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _region = string.IsNullOrWhiteSpace(region) ? "us-east-2" : region;
        }

        // Ensure cluster (idempotent)
        public async Task<EnsureEcsResult> EnsureClusterAsync(EnsureEcsClusterRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ct.ThrowIfCancellationRequested();

            lock (_stateLock) { _lastRequest = new EnsureEcsRequest { ClusterLogicalName = req.ClusterLogicalName }; _lastResult = null; }

            var clusterName = EnsureUtils.buildCanonicalName(req.ClusterLogicalName);

            // Try describe cluster
            var desc = await _ecs.DescribeClustersAsync(new DescribeClustersRequest { Clusters = new List<string> { clusterName } }, ct).ConfigureAwait(false);
            if (desc.Clusters != null && desc.Clusters.Count > 0 && desc.Clusters[0].Status == "ACTIVE")
            {
                var existing = desc.Clusters[0];
                var resultExists = new EnsureEcsResult { Created = false, AlreadyExisted = true, Profile = new EcsProfile { ClusterName = clusterName }, Message = "Cluster exists", LogRecord = null };
                lock (_stateLock) { _lastResult = resultExists; }
                Console.WriteLine($"[EnsureECS] Cluster already exists: {clusterName}");
                return resultExists;
            }

            // create cluster with tags
            var createReq = new CreateClusterRequest
            {
                ClusterName = clusterName,
                Tags = new List<Tag> { new Tag { Key = "Project", Value = EnsureUtils.canonicalPrefix }, new Tag { Key = "Name", Value = clusterName } }
            };

            var createResp = await _ecs.CreateClusterAsync(createReq, ct).ConfigureAwait(false);
            var cluster = createResp.Cluster;

            var profile = new EcsProfile { ClusterName = cluster.ClusterName, ClusterArn = cluster.ClusterArn };
            var serialized = JsonSerializer.Serialize(profile);
            var rec = EnsureUtils.makeResourceRecord(EnsureIdentifier, ResourceTypeName, $"{clusterName}-ecs-profile", serialized, _region);
            try { await _logger.appendAsync(rec).ConfigureAwait(false); } catch (Exception ex) { Console.WriteLine($"[EnsureECS] Warning: failed to append log: {ex.Message}"); }

            var result = new EnsureEcsResult { Created = true, AlreadyExisted = false, Profile = profile, Message = "Cluster created", LogRecord = rec };
            lock (_stateLock) { _lastResult = result; }
            Console.WriteLine($"[EnsureECS] Created cluster {clusterName}");
            return result;
        }

        // Ensure TaskDefinition (register); does not run tasks
        public async Task<EnsureTaskDefResult> EnsureTaskDefinitionAsync(EnsureEcsTaskDefRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ct.ThrowIfCancellationRequested();

            var family = EnsureUtils.buildCanonicalName(req.Family);
            lock (_stateLock) { _lastRequest = new EnsureEcsRequest { ClusterLogicalName = req.ClusterLogicalName }; _lastResult = null; }

            // Register CloudWatch log group (idempotent)
            var logGroupName = $"/ecs/{family}";
            try
            {
                await _logs.CreateLogGroupAsync(new CreateLogGroupRequest { LogGroupName = logGroupName, Tags = new Dictionary<string, string> { { "Project", EnsureUtils.canonicalPrefix } } }, ct).ConfigureAwait(false);
            }
            catch (ResourceAlreadyExistsException) { }
            catch { /* ignore other errors */ }

            // Register task definition (idempotent by family: create new revision each call)
            var containerDefs = new List<ContainerDefinition>
            {
                new ContainerDefinition
                {
                    Name = req.ContainerName,
                    Image = req.Image,
                    Essential = true,
                    Memory = req.Memory,
                    Cpu = req.Cpu,
                    PortMappings = req.PortMappings?.Select(pm => new PortMapping { ContainerPort = pm.ContainerPort, HostPort = pm.HostPort, Protocol = pm.Protocol }).ToList() ?? new List<PortMapping>(),
                    LogConfiguration = new LogConfiguration { LogDriver = LogDriver.Awslogs, Options = new Dictionary<string, string>
                        {
                            { "awslogs-group", logGroupName },
                            { "awslogs-region", _region },
                            { "awslogs-stream-prefix", family }
                        } }
                }
            };

            var registerReq = new RegisterTaskDefinitionRequest
            {
                Family = family,
                NetworkMode = NetworkMode.Awsvpc,
                RequiresCompatibilities = new List<string> { "FARGATE" },
                Cpu = req.Cpu.ToString(),
                Memory = req.Memory.ToString(),
                ExecutionRoleArn = req.ExecutionRoleArn,
                TaskRoleArn = req.TaskRoleArn,
                ContainerDefinitions = containerDefs
            };

            var regResp = await _ecs.RegisterTaskDefinitionAsync(registerReq, ct).ConfigureAwait(false);
            var td = regResp.TaskDefinition;

            var tdProfile = new TaskDefProfile { Family = family, TaskDefinitionArn = td.TaskDefinitionArn };
            var serialized = JsonSerializer.Serialize(tdProfile);
            var rec = EnsureUtils.makeResourceRecord(EnsureIdentifier, "EcsTaskDefinition", $"{family}-taskdef", serialized, _region);
            try { await _logger.appendAsync(rec).ConfigureAwait(false); } catch (Exception ex) { Console.WriteLine($"[EnsureECS] Warning: failed to append log: {ex.Message}"); }

            var result = new EnsureTaskDefResult { Created = true, TaskDef = tdProfile, Message = "TaskDefinition registered", LogRecord = rec };
            Console.WriteLine($"[EnsureECS] Registered task definition family={family} arn={td.TaskDefinitionArn}");
            return result;
        }

        // Ensure Service (optional): creates/updates a service running the task definition on Fargate
        public async Task<EnsureServiceResult> EnsureServiceAsync(EnsureEcsServiceRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ct.ThrowIfCancellationRequested();

            var clusterName = EnsureUtils.buildCanonicalName(req.ClusterLogicalName);
            var serviceName = EnsureUtils.buildCanonicalName(req.ServiceName);

            // Check service
            var describe = await _ecs.DescribeServicesAsync(new DescribeServicesRequest { Cluster = clusterName, Services = new List<string> { serviceName } }, ct).ConfigureAwait(false);
            if (describe.Services != null && describe.Services.Count > 0 && describe.Services[0].Status == "ACTIVE")
            {
                Console.WriteLine($"[EnsureECS] Service already exists: {serviceName} in cluster {clusterName}");
                return new EnsureServiceResult { Created = false, AlreadyExisted = true, Message = "Service exists" };
            }

            // Create service
            var createReq = new CreateServiceRequest
            {
                Cluster = clusterName,
                ServiceName = serviceName,
                TaskDefinition = req.TaskDefinitionArn,
                DesiredCount = req.DesiredCount,
                LaunchType = LaunchType.FARGATE,
                NetworkConfiguration = new NetworkConfiguration
                {
                    AwsvpcConfiguration = new AwsVpcConfiguration { AssignPublicIp = req.AssignPublicIp ? AssignPublicIp.ENABLED : AssignPublicIp.DISABLED, SecurityGroups = req.SecurityGroupIds?.ToList() ?? new List<string>(), Subnets = req.SubnetIds?.ToList() ?? new List<string>() }
                }
            };

            try
            {
                var resp = await _ecs.CreateServiceAsync(createReq, ct).ConfigureAwait(false);
                var profile = new ServiceProfile { Cluster = clusterName, ServiceName = serviceName, ServiceArn = resp.Service.ServiceArn };
                var rec = EnsureUtils.makeResourceRecord(EnsureIdentifier, "EcsService", $"{serviceName}-service", JsonSerializer.Serialize(profile), _region);
                try { await _logger.appendAsync(rec).ConfigureAwait(false); } catch { }
                Console.WriteLine($"[EnsureECS] Created service {serviceName} in cluster {clusterName}");
                return new EnsureServiceResult { Created = true, Service = profile, Message = "Service created", LogRecord = rec };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureECS] Error creating service {serviceName}: {ex.Message}");
                return new EnsureServiceResult { Created = false, AlreadyExisted = false, Message = ex.Message };
            }
        }

        // EnsureExistsAsync (returns all EnsureECS records or filtered by idOrName)
        public Task<EnsureExistsSummary> EnsureExistsAsync(string? idOrName = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var all = _logger.readAll();
            var records = all.Where(r => string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(idOrName))
            {
                var key = idOrName.Trim();
                records = records.Where(r => string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Id, key, StringComparison.OrdinalIgnoreCase));
            }

            var entries = records.Select(r => new EnsureExistsEntry { Name = r.Name, Id = r.Id, ResourceType = r.ResourceType, LoggedAt = r.CreatedAt }).ToList();
            var summary = new EnsureExistsSummary { Entries = entries, Total = entries.Count, Found = entries.Count, Missing = 0 };
            return System.Threading.Tasks.Task.FromResult(summary);
        }

        // EnsureDestroyAsync: best-effort cleanup for resources created by EnsureECS (reads infralog entries)
        public async Task<EnsureDestroyResult> EnsureDestroyAsync(string? idOrName = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var all = _logger.readAll();
            var records = all.Where(r => string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(idOrName))
            {
                var key = idOrName.Trim();
                records = records.Where(r => string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Id, key, StringComparison.OrdinalIgnoreCase));
            }

            var removed = new List<ResourceRecord>();
            foreach (var rec in records.OrderByDescending(r => r.CreatedAt))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (rec.ResourceType == ResourceTypeName)
                    {
                        // EcsProfile stored in Id
                        var profile = JsonSerializer.Deserialize<EcsProfile>(rec.Id);
                        if (profile != null && !string.IsNullOrWhiteSpace(profile.ClusterArn))
                        {
                            // Attempt to delete services first
                            try
                            {
                                var services = await _ecs.ListServicesAsync(new ListServicesRequest { Cluster = profile.ClusterName }, ct).ConfigureAwait(false);
                                if (services.ServiceArns != null)
                                {
                                    foreach (var sArn in services.ServiceArns)
                                    {
                                        try
                                        {
                                            await _ecs.DeleteServiceAsync(new DeleteServiceRequest { Cluster = profile.ClusterName, Service = sArn, Force = true }, ct).ConfigureAwait(false);
                                        }
                                        catch { }
                                    }
                                }
                            }
                            catch { }

                            // delete cluster
                            try { await _ecs.DeleteClusterAsync(new DeleteClusterRequest { Cluster = profile.ClusterName }, ct).ConfigureAwait(false); } catch { }
                        }
                    }
                    else if (rec.ResourceType == "EcsTaskDefinition")
                    {
                        // deregister task definition
                        try
                        {
                            await _ecs.DeregisterTaskDefinitionAsync(new DeregisterTaskDefinitionRequest { TaskDefinition = rec.Id }, ct).ConfigureAwait(false);
                        }
                        catch { }
                    }
                    else if (rec.ResourceType == "EcsService")
                    {
                        // delete service if possible
                        try
                        {
                            var profile = JsonSerializer.Deserialize<ServiceProfile>(rec.Id);
                            if (profile != null)
                            {
                                await _ecs.DeleteServiceAsync(new DeleteServiceRequest { Cluster = profile.Cluster, Service = profile.ServiceName, Force = true }, ct).ConfigureAwait(false);
                            }
                        }
                        catch { }
                    }

                    // remove only this ensure's log line(s)
                    TryRemoveLogRecord(rec);
                    removed.Add(rec);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureECS] Error destroying record {rec.Name}: {ex.Message}");
                }
            }

            return new EnsureDestroyResult { Removed = removed, RemovedCount = removed.Count, Message = removed.Count > 0 ? "Destroy attempts complete" : "No records removed" };
        }

        public override string ToString()
        {
            lock (_stateLock)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("EnsureECS Snapshot:");
                if (_lastRequest != null) sb.AppendLine($" ClusterLogicalName={_lastRequest.ClusterLogicalName}");
                else sb.AppendLine(" Request=null");
                if (_lastResult != null) sb.AppendLine($" Created={_lastResult.Created} Message={_lastResult.Message}");
                else sb.AppendLine(" Result=null");
                return sb.ToString();
            }
        }

        #region helpers

        private void TryRemoveLogRecord(ResourceRecord rec)
        {
            try
            {
                var lines = _logger.readAll()?.ToList() ?? new List<ResourceRecord>();
                var matches = lines.Where(r =>
                    string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.ResourceType, rec.ResourceType, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.Name, rec.Name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.Id, rec.Id, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matches.Count == 0) return;
                var kept = lines.Except(matches).ToList();
                _logger.clear();
                foreach (var k in kept) _logger.appendAsync(k).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureECS] Warning: failed to remove log record for {rec.Name}: {ex.Message}");
            }
        }

        #endregion

        #region DTOs / VOs

        public sealed class EnsureEcsClusterRequest
        {
            public string ClusterLogicalName { get; init; } = "ecs";
        }

        public sealed class EnsureEcsTaskDefRequest
        {
            public string ClusterLogicalName { get; init; } = "ecs";
            public string Family { get; init; } = "app";
            public string ContainerName { get; init; } = "app";
            public string Image { get; init; } = "nginx:latest";
            public int Cpu { get; init; } = 256;
            public int Memory { get; init; } = 512;
            public List<PortMappingRequest>? PortMappings { get; init; }
            public string? ExecutionRoleArn { get; init; }
            public string? TaskRoleArn { get; init; }
        }

        public sealed class PortMappingRequest { public int ContainerPort { get; init; } public int HostPort { get; init; } public string Protocol { get; init; } = "tcp"; }

        public sealed class EnsureEcsServiceRequest
        {
            public string ClusterLogicalName { get; init; } = "ecs";
            public string ServiceName { get; init; } = "app-service";
            public string TaskDefinitionArn { get; init; } = string.Empty;
            public int DesiredCount { get; init; } = 1;
            public IEnumerable<string>? SubnetIds { get; init; }
            public IEnumerable<string>? SecurityGroupIds { get; init; }
            public bool AssignPublicIp { get; init; } = false;
        }

        public sealed class EcsProfile { public string ClusterName { get; init; } = string.Empty; public string? ClusterArn { get; init; } }
        public sealed class TaskDefProfile { public string Family { get; init; } = string.Empty; public string? TaskDefinitionArn { get; init; } }
        public sealed class ServiceProfile { public string Cluster { get; init; } = string.Empty; public string ServiceName { get; init; } = string.Empty; public string? ServiceArn { get; init; } }

        public sealed class EnsureEcsRequest { public string? ClusterLogicalName { get; init; } }
        public sealed class EnsureEcsResult { public bool Created { get; init; } public bool AlreadyExisted { get; init; } public EcsProfile? Profile { get; init; } public string? Message { get; init; } public ResourceRecord? LogRecord { get; init; } }
        public sealed class EnsureTaskDefResult { public bool Created { get; init; } public TaskDefProfile? TaskDef { get; init; } public string? Message { get; init; } public ResourceRecord? LogRecord { get; init; } }
        public sealed class EnsureServiceResult { public bool Created { get; init; } public bool AlreadyExisted { get; init; } public ServiceProfile? Service { get; init; } public string? Message { get; init; } public ResourceRecord? LogRecord { get; init; } }

        public sealed class EnsureExistsEntry { public string Name { get; init; } = string.Empty; public string Id { get; init; } = string.Empty; public string ResourceType { get; init; } = string.Empty; public DateTime LoggedAt { get; init; } }
        public sealed class EnsureExistsSummary { public IReadOnlyList<EnsureExistsEntry> Entries { get; init; } = Array.Empty<EnsureExistsEntry>(); public int Total { get; init; } public int Found { get; init; } public int Missing { get; init; } }
        public sealed class EnsureDestroyResult { public IReadOnlyList<ResourceRecord> Removed { get; init; } = Array.Empty<ResourceRecord>(); public int RemovedCount { get; init; } public string? Message { get; init; } }

        #endregion
    }
}


// src/IaC_ProjectsPlus/EnsureModules/EnsureECS.cs
//
// EnsureECS
// - Ensures ECS cluster, CloudWatch log group, TaskDefinition (register), and optional Service exist.
// - Uses EnsureUtils.buildCanonicalName and EnsureUtils.makeResourceRecord to persist ResourceRecord entries with EnsureIdentifier = "EnsureECS".
// - Tags created resources with canonical project tag (EnsureUtils.canonicalPrefix).
// - Writes single-line ResourceRecord entries via Infralogger and logs to Console for visibility.
// - Conservative: registers TaskDefinition but does not force-run tasks unless EnsureServiceAsync is called.
// - Designed to be testable and conservative for deployment into ECS Fargate.
//
// Public API (high level)
// - Task<EnsureEcsResult> EnsureClusterAsync(EnsureEcsClusterRequest req, CancellationToken ct = default)
// - Task<EnsureTaskDefResult> EnsureTaskDefinitionAsync(EnsureEcsTaskDefRequest req, CancellationToken ct = default)
// - Task<EnsureServiceResult> EnsureServiceAsync(EnsureEcsServiceRequest req, CancellationToken ct = default)
// - Task<EnsureExistsSummary> EnsureExistsAsync(string? idOrName = null, CancellationToken ct = default)
// - Task<EnsureDestroyResult> EnsureDestroyAsync(string? idOrName = null, CancellationToken ct = default)
//
// Notes
// - This file depends on AWS SDK interfaces: IAmazonECS and IAmazonCloudWatchLogs.
// - It relies on EnsureUtils and Infralogger (EnsureUtils.makeResourceRecord, EnsureUtils.buildCanonicalName).
// - Service creation uses Fargate by default and expects appropriate IAM roles to exist; use EnsureIAM to provision those roles/policies.
//