using System.Text.Json;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.RDS.Model;
using Filter = Amazon.EC2.Model.Filter;
using Tag = Amazon.EC2.Model.Tag;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules
{
    public sealed class EnsureVPC
    {
        private readonly IAmazonEC2 _ec2;
        private readonly Infralogger _logger;
        private readonly string _region;

        private const string EnsureIdentifier = "EnsureVPC";
        private const string ResourceTypeName = "VpcProfile";

        // small state for ToString snapshot (deterministic for tests)
        private EnsureVpcRequest? _lastRequest;
        private EnsureVpcResult? _lastResult;
        private readonly object _stateLock = new();

        public EnsureVPC(IAmazonEC2 ec2, Infralogger logger, string region)
        {
            _ec2 = ec2 ?? throw new ArgumentNullException(nameof(ec2));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _region = string.IsNullOrWhiteSpace(region) ? "us-east-2" : region;
        }

        // EnsureVpcAsync: create or return existing profile
        public async Task<EnsureVpcResult> EnsureVpcAsync(EnsureVpcRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ct.ThrowIfCancellationRequested();

            lock (_stateLock) { _lastRequest = req; _lastResult = null; }

            var logicalPrefix = EnsureUtils.buildCanonicalName(req.LogicalNamePrefix);

            // Attempt to find latest profile log entry for this logicalPrefix
            var found = _logger.readAll()
                        .Where(r => string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(r.ResourceType, ResourceTypeName, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(r.Name, $"{logicalPrefix}-vpc-profile", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(r => r.CreatedAt)
                        .FirstOrDefault();

            if (found != null)
            {
                try
                {
                    var profile = JsonSerializer.Deserialize<VpcProfile>(found.Id);
                    if (profile != null)
                    {
                        var vpcExists = await VpcExistsAsync(profile.VpcId, ct).ConfigureAwait(false);
                        if (vpcExists)
                        {
                            var resultExists = new EnsureVpcResult { Created = false, AlreadyExisted = true, Profile = profile, Message = "Found existing VPC profile from log", LogRecord = found };
                            lock (_stateLock) { _lastResult = resultExists; }
                            Console.WriteLine($"[EnsureVPC] Found existing VPC: {profile.VpcId}");
                            return resultExists;
                        }
                    }
                }
                catch
                {
                    // fall through to create
                }
            }

            // Normalize CIDR lists to arrays for safe indexing
            var publicCidrs = (req.PublicCidrs?.ToArray()) ?? new[] { "10.20.1.0/24", "10.20.2.0/24" };
            var privateCidrs = (req.PrivateCidrs?.ToArray()) ?? new[] { "10.20.11.0/24", "10.20.12.0/24" };
            var isolatedCidrs = (req.IsolatedCidrs?.ToArray()) ?? new[] { "10.20.21.0/24", "10.20.22.0/24" };

            // Create or reuse VPC (tagged)
            var vpcTag = $"{logicalPrefix}-vpc";
            var vpcId = await CreateOrFindVpcAsync(vpcTag, req.Cidr ?? "10.20.0.0/16", ct).ConfigureAwait(false);

            // Determine AZs (take up to 2)
            var azResp = await _ec2.DescribeAvailabilityZonesAsync(new DescribeAvailabilityZonesRequest(), ct).ConfigureAwait(false);
            var azs = azResp.AvailabilityZones?.Select(a => a.ZoneName).Where(n => !string.IsNullOrWhiteSpace(n)).Take(2).ToArray() ?? Array.Empty<string>();
            if (azs.Length == 0) throw new InvalidOperationException("No availability zones found in region");

            // helper: ensure subnet
            async Task<string> EnsureSubnet(string cidr, string nameSuffix, string az, bool mapPublic)
            {
                var tagName = $"{logicalPrefix}-{nameSuffix}";
                try
                {
                    var desc = await _ec2.DescribeSubnetsAsync(new DescribeSubnetsRequest
                    {
                        Filters = new List<Filter>
                        {
                            new Filter("vpc-id", new List<string>{ vpcId }),
                            new Filter("cidr-block", new List<string>{ cidr })
                        }
                    }, ct).ConfigureAwait(false);

                    if (desc.Subnets != null && desc.Subnets.Count > 0)
                    {
                        var existing = desc.Subnets[0].SubnetId;
                        await TagResourceSafeAsync(existing, tagName, ct).ConfigureAwait(false);
                        return existing;
                    }
                }
                catch { /* ignore and create */ }

                var create = await _ec2.CreateSubnetAsync(new CreateSubnetRequest { VpcId = vpcId, CidrBlock = cidr, AvailabilityZone = az }, ct).ConfigureAwait(false);
                var subnetId = create.Subnet.SubnetId;
                try { await _ec2.ModifySubnetAttributeAsync(new ModifySubnetAttributeRequest { SubnetId = subnetId, MapPublicIpOnLaunch = mapPublic }, ct).ConfigureAwait(false); } catch { }
                await TagResourceSafeAsync(subnetId, $"{logicalPrefix}-{nameSuffix}", ct).ConfigureAwait(false);
                return subnetId;
            }

            // create public subnets
            var publicSub1 = await EnsureSubnet(publicCidrs[0], "public-a", azs[0], true).ConfigureAwait(false);
            var publicSub2 = await EnsureSubnet(publicCidrs.Length > 1 ? publicCidrs[1] : publicCidrs[0], "public-b", azs.Length > 1 ? azs[1] : azs[0], true).ConfigureAwait(false);

            // private subnets
            var privSub1 = await EnsureSubnet(privateCidrs[0], "private-a", azs[0], false).ConfigureAwait(false);
            var privSub2 = await EnsureSubnet(privateCidrs.Length > 1 ? privateCidrs[1] : privateCidrs[0], "private-b", azs.Length > 1 ? azs[1] : azs[0], false).ConfigureAwait(false);

            // isolated subnets
            var isoSub1 = await EnsureSubnet(isolatedCidrs[0], "isolated-a", azs[0], false).ConfigureAwait(false);
            var isoSub2 = await EnsureSubnet(isolatedCidrs.Length > 1 ? isolatedCidrs[1] : isolatedCidrs[0], "isolated-b", azs.Length > 1 ? azs[1] : azs[0], false).ConfigureAwait(false);

            // Internet gateway attach/create
            var igwId = await EnsureInternetGatewayAttachedAsync(vpcId, $"{logicalPrefix}-igw", ct).ConfigureAwait(false);

            // Route tables
            var publicRt = await EnsureRouteTableAsync(vpcId, $"{logicalPrefix}-rt-public", igwId, null, ct).ConfigureAwait(false);
            var privateRt = await EnsureRouteTableAsync(vpcId, $"{logicalPrefix}-rt-private", null, null, ct).ConfigureAwait(false);
            var isolatedRt = await EnsureRouteTableAsync(vpcId, $"{logicalPrefix}-rt-isolated", null, null, ct).ConfigureAwait(false);

            // Associate route tables
            async Task AssociateIfMissing(string rtId, string subnetId)
            {
                try
                {
                    var desc = await _ec2.DescribeRouteTablesAsync(new DescribeRouteTablesRequest { Filters = new List<Filter> { new Filter("association.subnet-id", new List<string> { subnetId }) } }, ct).ConfigureAwait(false);
                    if (desc.RouteTables == null || desc.RouteTables.Count == 0)
                    {
                        await _ec2.AssociateRouteTableAsync(new AssociateRouteTableRequest { RouteTableId = rtId, SubnetId = subnetId }, ct).ConfigureAwait(false);
                    }
                }
                catch { }
            }

            await AssociateIfMissing(publicRt, publicSub1).ConfigureAwait(false);
            await AssociateIfMissing(publicRt, publicSub2).ConfigureAwait(false);
            await AssociateIfMissing(privateRt, privSub1).ConfigureAwait(false);
            await AssociateIfMissing(privateRt, privSub2).ConfigureAwait(false);
            await AssociateIfMissing(isolatedRt, isoSub1).ConfigureAwait(false);
            await AssociateIfMissing(isolatedRt, isoSub2).ConfigureAwait(false);

            // Security groups
            var appSg = await EnsureSecurityGroupAsync(vpcId, $"{logicalPrefix}-app-sg", "Application instances", new List<IpPermission>
            {
                new IpPermission { IpProtocol="tcp", FromPort=80, ToPort=80, Ipv4Ranges = new List<IpRange>{ new IpRange{ CidrIp="0.0.0.0/0" } } },
                new IpPermission { IpProtocol="tcp", FromPort=443, ToPort=443, Ipv4Ranges = new List<IpRange>{ new IpRange{ CidrIp="0.0.0.0/0" } } }
            }, ct).ConfigureAwait(false);

            var albSg = await EnsureSecurityGroupAsync(vpcId, $"{logicalPrefix}-alb-sg", "Load balancer", new List<IpPermission>
            {
                new IpPermission { IpProtocol="tcp", FromPort=80, ToPort=80, Ipv4Ranges = new List<IpRange>{ new IpRange{ CidrIp="0.0.0.0/0" } } },
                new IpPermission { IpProtocol="tcp", FromPort=443, ToPort=443, Ipv4Ranges = new List<IpRange>{ new IpRange{ CidrIp="0.0.0.0/0" } } }
            }, ct).ConfigureAwait(false);

            var rdsSg = await EnsureSecurityGroupAsync(vpcId, $"{logicalPrefix}-rds-sg", "RDS access", null, ct).ConfigureAwait(false);

            var callerCidr = string.IsNullOrWhiteSpace(req.CallerCidr) ? "0.0.0.0/0" : req.CallerCidr.Trim();
            var bastionIngress = string.IsNullOrWhiteSpace(callerCidr) ? null : new List<IpPermission> { new IpPermission { IpProtocol = "tcp", FromPort = 22, ToPort = 22, Ipv4Ranges = new List<IpRange> { new IpRange { CidrIp = callerCidr } } } };
            var bastionSg = await EnsureSecurityGroupAsync(vpcId, $"{logicalPrefix}-bastion-sg", "Bastion host", bastionIngress, ct).ConfigureAwait(false);

            // NAT gateways (best-effort)
            var natIds = new List<string>();
            if (req.CreateNatGateways)
            {
                foreach (var pubSubnet in new[] { publicSub1, publicSub2 })
                {
                    try
                    {
                        var alloc = await _ec2.AllocateAddressAsync(new AllocateAddressRequest { Domain = DomainType.Vpc }, ct).ConfigureAwait(false);
                        var natCreate = await _ec2.CreateNatGatewayAsync(new CreateNatGatewayRequest { SubnetId = pubSubnet, AllocationId = alloc.AllocationId }, ct).ConfigureAwait(false);
                        if (natCreate.NatGateway != null && !string.IsNullOrWhiteSpace(natCreate.NatGateway.NatGatewayId))
                        {
                            natIds.Add(natCreate.NatGateway.NatGatewayId);
                        }
                    }
                    catch { /* continue */ }
                }
            }

            if (natIds.Count > 0)
            {
                try { await _ec2.CreateRouteAsync(new CreateRouteRequest { RouteTableId = privateRt, NatGatewayId = natIds[0], DestinationCidrBlock = "0.0.0.0/0" }, ct).ConfigureAwait(false); } catch { }
            }

            // Build final profile
            var createdProfile = new VpcProfile
            {
                Region = _region,
                VpcId = vpcId,
                PublicSubnetIds = new List<string> { publicSub1, publicSub2 },
                PrivateSubnetIds = new List<string> { privSub1, privSub2 },
                IsolatedSubnetIds = new List<string> { isoSub1, isoSub2 },
                AppSecurityGroupId = appSg,
                RdsSecurityGroupId = rdsSg,
                LoadBalancerSecurityGroupId = albSg,
                BastionSecurityGroupId = bastionSg,
                RouteTableIds = new List<string> { publicRt, privateRt, isolatedRt },
                NatGatewayIds = natIds,
                InternetGatewayId = igwId
            };

            // Serialize profile and write log record
            var serialized = JsonSerializer.Serialize(createdProfile);
            var record = EnsureUtils.makeResourceRecord(EnsureIdentifier, ResourceTypeName, $"{logicalPrefix}-vpc-profile", serialized, _region);
            try
            {
                await _logger.appendAsync(record).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureVPC] Warning: failed to append infralog: {ex.Message}");
            }

            var result = new EnsureVpcResult { Created = true, AlreadyExisted = false, Profile = createdProfile, Message = "VPC created and profiled", LogRecord = record };
            lock (_stateLock) { _lastResult = result; }
            Console.WriteLine($"[EnsureVPC] Created VPC {vpcId} and wrote profile log.");
            return result;
        }

        // EnsureExistsAsync: return summaries for EnsureVPC records; filter by idOrName when provided
        public async Task<EnsureVpcExistsSummary> EnsureExistsAsync(string? idOrName = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var all = _logger.readAll();
            var records = all.Where(r => string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(r.ResourceType, ResourceTypeName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(idOrName))
            {
                var key = idOrName.Trim();
                records = records.Where(r => string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Id, key, StringComparison.OrdinalIgnoreCase));
            }

            var list = new List<EnsureVpcExistsEntry>();
            foreach (var rec in records)
            {
                ct.ThrowIfCancellationRequested();
                VpcProfile? profile = null;
                try { profile = JsonSerializer.Deserialize<VpcProfile>(rec.Id); } catch { }

                var vpcExists = false;
                if (profile != null && !string.IsNullOrWhiteSpace(profile.VpcId))
                {
                    vpcExists = await VpcExistsAsync(profile.VpcId, ct).ConfigureAwait(false);
                }

                list.Add(new EnsureVpcExistsEntry { Profile = profile, LoggedAt = rec.CreatedAt, ExistsInCloud = vpcExists, RecordName = rec.Name, RecordId = rec.Id });
            }

            var summary = new EnsureVpcExistsSummary { Entries = list, Total = list.Count, Found = list.Count(e => e.ExistsInCloud), Missing = list.Count(e => !e.ExistsInCloud) };
            return summary;
        }

        // EnsureDestroyAsync: best-effort cleanup for records matching idOrName (or all if null)
        public async Task<EnsureVpcDestroyResult> EnsureDestroyAsync(string? idOrName = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var all = _logger.readAll();
            var records = all.Where(r => string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(r.ResourceType, ResourceTypeName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(idOrName))
            {
                var key = idOrName.Trim();
                records = records.Where(r => string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Id, key, StringComparison.OrdinalIgnoreCase));
            }

            var removed = new List<ResourceRecord>();
            foreach (var rec in records.OrderByDescending(r => r.CreatedAt))
            {
                ct.ThrowIfCancellationRequested();
                VpcProfile? profile = null;
                try { profile = JsonSerializer.Deserialize<VpcProfile>(rec.Id); } catch { }

                if (profile == null)
                {
                    TryRemoveLogRecord(rec);
                    continue;
                }

                try
                {
                    // Delete NAT gateways
                    foreach (var nat in profile.NatGatewayIds ?? Enumerable.Empty<string>())
                    {
                        try { await _ec2.DeleteNatGatewayAsync(new DeleteNatGatewayRequest { NatGatewayId = nat }, ct).ConfigureAwait(false); } catch { }
                    }

                    await Task.Delay(2000, ct).ConfigureAwait(false);

                    // Disassociate & delete route tables (skip main)
                    try
                    {
                        var rts = await _ec2.DescribeRouteTablesAsync(new DescribeRouteTablesRequest { Filters = new List<Filter> { new Filter("vpc-id", new List<string> { profile.VpcId }) } }, ct).ConfigureAwait(false);
                        foreach (var rt in rts.RouteTables)
                        {
                            var isMain = rt.Associations != null && rt.Associations.Any(a => a.Main == true);
                            if (isMain) continue;

                            if (rt.Associations != null)
                            {
                                foreach (var assoc in rt.Associations.Where(a => !string.IsNullOrWhiteSpace(a.RouteTableAssociationId)))
                                {
                                    try { await _ec2.DisassociateRouteTableAsync(new DisassociateRouteTableRequest { AssociationId = assoc.RouteTableAssociationId }, ct).ConfigureAwait(false); } catch { }
                                }
                            }

                            try { await _ec2.DeleteRouteTableAsync(new DeleteRouteTableRequest { RouteTableId = rt.RouteTableId }, ct).ConfigureAwait(false); } catch { }
                        }
                    }
                    catch { }

                    // Release Elastic IPs (best-effort)
                    try
                    {
                        var addrs = await _ec2.DescribeAddressesAsync(new DescribeAddressesRequest(), ct).ConfigureAwait(false);
                        foreach (var a in addrs.Addresses)
                        {
                            try
                            {
                                if (!string.IsNullOrWhiteSpace(a.AllocationId) && a.AssociationId == null && a.Domain == DomainType.Vpc)
                                {
                                    await _ec2.ReleaseAddressAsync(new ReleaseAddressRequest { AllocationId = a.AllocationId }, ct).ConfigureAwait(false);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }

                    // Delete subnets
                    foreach (var s in (profile.PublicSubnetIds ?? Enumerable.Empty<string>()).Concat(profile.PrivateSubnetIds ?? Enumerable.Empty<string>()).Concat(profile.IsolatedSubnetIds ?? Enumerable.Empty<string>()))
                    {
                        try { await _ec2.DeleteSubnetAsync(new DeleteSubnetRequest { SubnetId = s }, ct).ConfigureAwait(false); } catch { }
                    }

                    // Delete security groups (ignore default)
                    try
                    {
                        var sgIds = new[] { profile.AppSecurityGroupId, profile.RdsSecurityGroupId, profile.LoadBalancerSecurityGroupId, profile.BastionSecurityGroupId }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                        foreach (var sg in sgIds)
                        {
                            try { await _ec2.DeleteSecurityGroupAsync(new DeleteSecurityGroupRequest { GroupId = sg }, ct).ConfigureAwait(false); } catch { }
                        }
                    }
                    catch { }

                    // Detach & delete IGW
                    if (!string.IsNullOrWhiteSpace(profile.InternetGatewayId))
                    {
                        try { await _ec2.DetachInternetGatewayAsync(new DetachInternetGatewayRequest { InternetGatewayId = profile.InternetGatewayId, VpcId = profile.VpcId }, ct).ConfigureAwait(false); } catch { }
                        try { await _ec2.DeleteInternetGatewayAsync(new DeleteInternetGatewayRequest { InternetGatewayId = profile.InternetGatewayId }, ct).ConfigureAwait(false); } catch { }
                    }

                    // Delete VPC
                    try { await _ec2.DeleteVpcAsync(new DeleteVpcRequest { VpcId = profile.VpcId }, ct).ConfigureAwait(false); } catch { }

                    // Remove log record for this EnsureIdentifier only
                    TryRemoveLogRecord(rec);
                    removed.Add(rec);
                    Console.WriteLine($"[EnsureVPC] Destroyed VPC and removed log for {profile.VpcId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureVPC] Error during destroy for {rec.Name}: {ex.Message}");
                }
            }

            return new EnsureVpcDestroyResult { RemovedRecords = removed, RemovedCount = removed.Count, Message = removed.Count > 0 ? "Destroy attempts completed" : "No records destroyed" };
        }

        public override string ToString()
        {
            lock (_stateLock)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("EnsureVPC Snapshot:");
                if (_lastRequest != null)
                {
                    sb.AppendLine($" LogicalNamePrefix={_lastRequest.LogicalNamePrefix}");
                    sb.AppendLine($" CallerCidr={_lastRequest.CallerCidr}");
                    sb.AppendLine($" CreateNatGateways={_lastRequest.CreateNatGateways}");
                }
                else sb.AppendLine(" Request=null");

                if (_lastResult != null)
                {
                    sb.AppendLine($" Created={_lastResult.Created}");
                    sb.AppendLine($" AlreadyExisted={_lastResult.AlreadyExisted}");
                    sb.AppendLine($" VpcId={_lastResult.Profile?.VpcId}");
                }
                else sb.AppendLine(" Result=null");

                return sb.ToString();
            }
        }

        #region internal helpers

        private async Task<string> CreateOrFindVpcAsync(string vpcTagName, string cidr, CancellationToken ct)
        {
            try
            {
                var found = await _ec2.DescribeVpcsAsync(new DescribeVpcsRequest { Filters = new List<Filter> { new Filter("tag:Name", new List<string> { vpcTagName }) } }, ct).ConfigureAwait(false);
                if (found.Vpcs != null && found.Vpcs.Count > 0) return found.Vpcs[0].VpcId;
            }
            catch { /* continue to create */ }

            var create = await _ec2.CreateVpcAsync(new CreateVpcRequest { CidrBlock = cidr }, ct).ConfigureAwait(false);
            var vpcId = create.Vpc.VpcId;
            try { await _ec2.CreateTagsAsync(new CreateTagsRequest { Resources = new List<string> { vpcId }, Tags = new List<Tag> { new Tag("Name", vpcTagName), new Tag("Project", EnsureUtils.canonicalPrefix) } }, ct).ConfigureAwait(false); } catch { }
            try { await _ec2.ModifyVpcAttributeAsync(new ModifyVpcAttributeRequest { VpcId = vpcId, EnableDnsHostnames = true }, ct).ConfigureAwait(false); } catch { }
            return vpcId;
        }

        private async Task<string> EnsureInternetGatewayAttachedAsync(string vpcId, string nameTag, CancellationToken ct)
        {
            try
            {
                var igwDesc = await _ec2.DescribeInternetGatewaysAsync(new DescribeInternetGatewaysRequest { Filters = new List<Filter> { new Filter("attachment.vpc-id", new List<string> { vpcId }) } }, ct).ConfigureAwait(false);
                if (igwDesc.InternetGateways != null && igwDesc.InternetGateways.Count > 0) return igwDesc.InternetGateways[0].InternetGatewayId;
            }
            catch { }

            var create = await _ec2.CreateInternetGatewayAsync(new CreateInternetGatewayRequest(), ct).ConfigureAwait(false);
            var igwId = create.InternetGateway.InternetGatewayId;
            try { await _ec2.AttachInternetGatewayAsync(new AttachInternetGatewayRequest { InternetGatewayId = igwId, VpcId = vpcId }, ct).ConfigureAwait(false); } catch { }
            try { await _ec2.CreateTagsAsync(new CreateTagsRequest { Resources = new List<string> { igwId }, Tags = new List<Tag> { new Tag("Name", nameTag), new Tag("Project", EnsureUtils.canonicalPrefix) } }, ct).ConfigureAwait(false); } catch { }
            return igwId;
        }

        private async Task<string> EnsureRouteTableAsync(string vpcId, string nameTag, string? igwId, string? natGatewayId, CancellationToken ct)
        {
            try
            {
                var resp = await _ec2.DescribeRouteTablesAsync(new DescribeRouteTablesRequest { Filters = new List<Filter> { new Filter("tag:Name", new List<string> { nameTag }), new Filter("vpc-id", new List<string> { vpcId }) } }, ct).ConfigureAwait(false);
                if (resp.RouteTables != null && resp.RouteTables.Count > 0) return resp.RouteTables[0].RouteTableId;
            }
            catch { }

            var created = await _ec2.CreateRouteTableAsync(new CreateRouteTableRequest { VpcId = vpcId }, ct).ConfigureAwait(false);
            var rtId = created.RouteTable.RouteTableId;
            try { await _ec2.CreateTagsAsync(new CreateTagsRequest { Resources = new List<string> { rtId }, Tags = new List<Tag> { new Tag("Name", nameTag), new Tag("Project", EnsureUtils.canonicalPrefix) } }, ct).ConfigureAwait(false); } catch { }

            if (!string.IsNullOrWhiteSpace(igwId))
            {
                try { await _ec2.CreateRouteAsync(new CreateRouteRequest { RouteTableId = rtId, GatewayId = igwId, DestinationCidrBlock = "0.0.0.0/0" }, ct).ConfigureAwait(false); } catch { }
            }
            else if (!string.IsNullOrWhiteSpace(natGatewayId))
            {
                try { await _ec2.CreateRouteAsync(new CreateRouteRequest { RouteTableId = rtId, NatGatewayId = natGatewayId, DestinationCidrBlock = "0.0.0.0/0" }, ct).ConfigureAwait(false); } catch { }
            }

            return rtId;
        }

        private async Task<string> EnsureSecurityGroupAsync(string vpcId, string name, string description, List<IpPermission>? ingress, CancellationToken ct)
        {
            try
            {
                var find = await _ec2.DescribeSecurityGroupsAsync(new DescribeSecurityGroupsRequest
                {
                    Filters = new List<Filter> {
                        new Filter("group-name", new List<string>{ name }),
                        new Filter("vpc-id", new List<string>{ vpcId })
                    }
                }, ct).ConfigureAwait(false);

                if (find.SecurityGroups != null && find.SecurityGroups.Count > 0) return find.SecurityGroups[0].GroupId;
            }
            catch { }

            var create = await _ec2.CreateSecurityGroupAsync(new CreateSecurityGroupRequest { GroupName = name, Description = description, VpcId = vpcId }, ct).ConfigureAwait(false);
            var gid = create.GroupId;

            if (ingress != null && ingress.Count > 0)
            {
                try { await _ec2.AuthorizeSecurityGroupIngressAsync(new AuthorizeSecurityGroupIngressRequest { GroupId = gid, IpPermissions = ingress }, ct).ConfigureAwait(false); } catch { }
            }

            try { await _ec2.CreateTagsAsync(new CreateTagsRequest { Resources = new List<string> { gid }, Tags = new List<Tag> { new Tag("Name", name), new Tag("Project", EnsureUtils.canonicalPrefix) } }, ct).ConfigureAwait(false); } catch { }
            return gid;
        }

        private async Task TagResourceSafeAsync(string resourceId, string nameTag, CancellationToken ct)
        {
            try
            {
                await _ec2.CreateTagsAsync(new CreateTagsRequest { Resources = new List<string> { resourceId }, Tags = new List<Tag> { new Tag("Name", nameTag), new Tag("Project", EnsureUtils.canonicalPrefix) } }, ct).ConfigureAwait(false);
            }
            catch { }
        }

        private async Task<bool> VpcExistsAsync(string vpcId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(vpcId)) return false;
            try
            {
                var resp = await _ec2.DescribeVpcsAsync(new DescribeVpcsRequest { VpcIds = new List<string> { vpcId } }, ct).ConfigureAwait(false);
                return resp.Vpcs != null && resp.Vpcs.Count > 0;
            }
            catch (AmazonEC2Exception ex)
            {
                // Common "not found" error code is "InvalidVpcID.NotFound"; treat as not found
                if (string.Equals(ex.ErrorCode, "InvalidVpcID.NotFound", StringComparison.OrdinalIgnoreCase) ||
                    (ex.Message?.IndexOf("InvalidVpcID", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return false;
                }

                Console.WriteLine($"[EnsureVPC] EC2 error checking VPC {vpcId}: {ex.Message}");
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Remove only log lines that match EnsureIdentifier + ResourceType + Name + Id
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
                Console.WriteLine($"[EnsureVPC] Warning: failed to remove log record for {rec.Name}: {ex.Message}");
            }
        }

        #endregion

        #region DTOs / VOs

        public static async Task<string> GetMyPublicIpAsync()
        {
            // get public IP (simple, reliable endpoint)
            using var http = new HttpClient();
            var ip = (await http.GetStringAsync("https://checkip.amazonaws.com")).Trim(); // e.g. "203.0.113.45"
            return $"{ip}/32";
        }

        public sealed class EnsureVpcRequest
        {
            // logical prefix (will be canonicalized)
            public string LogicalNamePrefix { get; init; } = "vpc";
            public string? Cidr { get; init; } = "10.20.0.0/16";
            public IEnumerable<string>? PublicCidrs { get; init; }
            public IEnumerable<string>? PrivateCidrs { get; init; }
            public IEnumerable<string>? IsolatedCidrs { get; init; }
            // restrict SSH/RDS access for testing; set to your IP/CIDR e.g., "203.0.113.5/32"
            public string? CallerCidr { get; init; } = null;
            public bool CreateNatGateways { get; init; } = false;
        }

        public sealed class VpcProfile
        {
            // Use settable properties to allow building the profile incrementally
            public string Region { get; set; } = string.Empty;
            public string VpcId { get; set; } = string.Empty;
            public IReadOnlyList<string> PublicSubnetIds { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> PrivateSubnetIds { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> IsolatedSubnetIds { get; set; } = Array.Empty<string>();
            public string AppSecurityGroupId { get; set; } = string.Empty;
            public string RdsSecurityGroupId { get; set; } = string.Empty;
            public string LoadBalancerSecurityGroupId { get; set; } = string.Empty;
            public string BastionSecurityGroupId { get; set; } = string.Empty;
            public IReadOnlyList<string> RouteTableIds { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> NatGatewayIds { get; set; } = Array.Empty<string>();
            public string? InternetGatewayId { get; set; }
        }

        public sealed class EnsureVpcResult
        {
            public bool Created { get; init; }
            public bool AlreadyExisted { get; init; }
            public VpcProfile? Profile { get; init; }
            public string? Message { get; init; }
            public ResourceRecord? LogRecord { get; init; }
        }

        public sealed class EnsureVpcExistsEntry
        {
            public VpcProfile? Profile { get; init; }
            public DateTime LoggedAt { get; init; }
            public bool ExistsInCloud { get; init; }
            public string? RecordName { get; init; }
            public string? RecordId { get; init; }
        }

        public sealed class EnsureVpcExistsSummary
        {
            public IReadOnlyList<EnsureVpcExistsEntry> Entries { get; init; } = Array.Empty<EnsureVpcExistsEntry>();
            public int Total { get; init; }
            public int Found { get; init; }
            public int Missing { get; init; }
        }

        public sealed class EnsureVpcDestroyResult
        {
            public IReadOnlyList<ResourceRecord> RemovedRecords { get; init; } = Array.Empty<ResourceRecord>();
            public int RemovedCount { get; init; }
            public string? Message { get; init; }
        }

        #endregion
    }
}


// src/IaC_ProjectsPlus/EnsureModules/EnsureVPC.cs
//
// EnsureVPC - error-free implementation
// - Idempotent EnsureVpcAsync, read-only EnsureExistsAsync, best-effort EnsureDestroyAsync
// - Uses EnsureUtils (buildCanonicalName, makeResourceRecord) and Infralogger for single-line ResourceRecord entries
// - Tags created resources with EnsureUtils.canonicalPrefix via CreateTags when supported
// - CallerCidr can be passed to restrict SSH/RDS access (useful for local testing)
// - Defensive EC2 exception handling (no dependency on non-portable SDK-specific exception types)
// - Arrays used for CIDR lists to avoid IEnumerable indexing issues
//
// NOTE: This file focuses on correctness and compile-time safety. It is intentionally conservative
// and uses best-effort cleanup semantics. Adapt logging, retries, and stronger error handling as needed.

// src/IaC_ProjectsPlus/EnsureModules/EnsureVPC.cs
//
// EnsureVPC
// - Idempotent EnsureVpcAsync, EnsureExistsAsync, EnsureDestroyAsync for VPC + subnets + route tables + IGW + NAT + security groups.
// - Uses EnsureUtils.buildCanonicalName and EnsureUtils.makeResourceRecord to persist ResourceRecord lines with EnsureIdentifier = "EnsureVPC".
// - Adds canonical project tag where applicable when creating resources.
// - Allows specifying callerCidr (your public IP/CIDR) so RDS or bastion security groups can be limited for local testing.
// - Emits Console logs for major operations and writes single-line ResourceRecord entries via Infralogger.
// - Best-effort destructive cleanup in EnsureDestroyAsync; reads log lines produced by EnsureVPC to know what to remove.
// - Uses AWS EC2 client (IAmazonEC2) for operations.
//
// Important: This file is a conservative, testable skeleton focused on orchestration and logging. It favors readability and safe ordering.
// Produces a serialized VpcProfile as ResourceRecord.Id so EnsureExistsAsync readers can deserialize and validate components.
//
// Minimal IAM permissions required (examples):
// - ec2:DescribeVpcs, ec2:CreateVpc, ec2:CreateSubnet, ec2:DescribeSubnets, ec2:CreateInternetGateway, ec2:AttachInternetGateway
// - ec2:CreateRouteTable, ec2:AssociateRouteTable, ec2:CreateSecurityGroup, ec2:AuthorizeSecurityGroupIngress
// - ec2:DescribeRouteTables, ec2:Delete* for cleanup operations
//
// Usage example:
//   var ensure = new EnsureVPC(ec2Client, new Infralogger(path), region);
//   var req = new EnsureVpcRequest { LogicalNamePrefix = "projects", CallerCidr = "203.0.113.5/32" };
//   var result = await ensure.EnsureVpcAsync(req);
//   var exists = await ensure.EnsureExistsAsync();
//   var destroyed = await ensure.EnsureDestroyAsync(req.LogicalNamePrefix);
//


