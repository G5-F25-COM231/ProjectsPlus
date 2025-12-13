using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.EC2;
using Amazon.EC2.Model;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules
{
    /// <summary>
    /// EnsureVpc.cs
    /// - Idempotent EnsureVpcAsync that discovers or creates VPC networking suitable for app + RDS.
    /// - Writes a single ResourceRecord entry with ResourceType = "EnsureVPC" and Id = serialized profile JSON.
    /// - Provides CheckLogsAsync to read the log and validate the profile against AWS state.
    /// - Provides CleanupVpcAsync which reads the profile from the log, deserializes it and deletes recorded resources (best-effort).
    /// Conventions:
    /// - InfraLogger.ReadAll() returns ResourceRecord items with at least: ResourceType, Name, Id, Region, CreatedAt.
    /// - ResourceRecord.Id is used to carry the serialized profile JSON for "VpcProfile" entries.
    /// </summary>
    public static class EnsureVPC
    {
        public const string LogResourceType = "EnsureVPC";

        // The profile that is serialized into ResourceRecord.Id for ResourceType="VpcProfile"
        public record EnsureVPCProfile(
            string Region,
            string VpcId,
            IReadOnlyList<string> PublicSubnetIds,
            IReadOnlyList<string> PrivateSubnetIds,
            IReadOnlyList<string> IsolatedSubnetIds,
            string AppSecurityGroupId,
            string RdsSecurityGroupId,
            string LoadBalancerSecurityGroupId,
            string BastionSecurityGroupId,
            IReadOnlyList<string> RouteTableIds,
            IReadOnlyList<string> NatGatewayIds,
            string? InternetGatewayId
        );

        public record EnsureVPCCheckResult(
            EnsureVPCProfile? Profile,
            bool VpcExists,
            bool PublicSubnetsExist,
            bool PrivateSubnetsExist,
            bool IsolatedSubnetsExist,
            bool SgsExist,
            bool RouteTablesExist,
            bool NatGatewaysExist,
            bool InternetGatewayExists
        );

        /// <summary>
        /// Read the logger for a VpcProfile entry (Name = {logicalNamePrefix}-vpc-profile).
        /// If found, deserialize profile (from ResourceRecord.Id) and validate presence of components in AWS.
        /// Returns a VpcCheckResult with booleans indicating what exists.
        /// </summary>
        public static async Task<EnsureVPCCheckResult> CheckLogsAsync(
            IAmazonEC2 ec2,
            Infralogger logger,
            string logicalNamePrefix)
        {
            if (ec2 == null) throw new ArgumentNullException(nameof(ec2));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(logicalNamePrefix)) throw new ArgumentNullException(nameof(logicalNamePrefix));

            var all = logger.ReadAll() ?? Enumerable.Empty<ResourceRecord>();
            var targetName = $"{logicalNamePrefix}-vpc-profile";
            var record = all.Reverse().FirstOrDefault(r =>
                string.Equals(r.ResourceType, LogResourceType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Name, targetName, StringComparison.OrdinalIgnoreCase));

            if (record == null || string.IsNullOrWhiteSpace(record.Id))
            {
                return new EnsureVPCCheckResult(null, false, false, false, false, false, false, false, false);
            }

            EnsureVPCProfile? profile;
            try
            {
                profile = JsonSerializer.Deserialize<EnsureVPCProfile>(record.Id);
            }
            catch
            {
                return new EnsureVPCCheckResult(null, false, false, false, false, false, false, false, false);
            }

            bool vpcExists = false, pubSubnets = false, privSubnets = false, isoSubnets = false, sgs = false, rts = false, nats = false, igw = false;

            try
            {
                if (!string.IsNullOrWhiteSpace(profile?.VpcId))
                {
                    var vpcDesc = await ec2.DescribeVpcsAsync(new DescribeVpcsRequest { VpcIds = new List<string> { profile.VpcId } }).ConfigureAwait(false);
                    vpcExists = vpcDesc.Vpcs != null && vpcDesc.Vpcs.Count > 0;
                }

                if (profile?.PublicSubnetIds != null && profile.PublicSubnetIds.Count > 0)
                {
                    var resp = await ec2.DescribeSubnetsAsync(new DescribeSubnetsRequest { SubnetIds = profile.PublicSubnetIds.ToList() }).ConfigureAwait(false);
                    pubSubnets = resp.Subnets != null && resp.Subnets.Count == profile.PublicSubnetIds.Count;
                }

                if (profile?.PrivateSubnetIds != null && profile.PrivateSubnetIds.Count > 0)
                {
                    var resp = await ec2.DescribeSubnetsAsync(new DescribeSubnetsRequest { SubnetIds = profile.PrivateSubnetIds.ToList() }).ConfigureAwait(false);
                    privSubnets = resp.Subnets != null && resp.Subnets.Count == profile.PrivateSubnetIds.Count;
                }

                if (profile?.IsolatedSubnetIds != null && profile.IsolatedSubnetIds.Count > 0)
                {
                    var resp = await ec2.DescribeSubnetsAsync(new DescribeSubnetsRequest { SubnetIds = profile.IsolatedSubnetIds.ToList() }).ConfigureAwait(false);
                    isoSubnets = resp.Subnets != null && resp.Subnets.Count == profile.IsolatedSubnetIds.Count;
                }

                var expectedSgIds = new[] { profile?.AppSecurityGroupId, profile?.RdsSecurityGroupId, profile?.LoadBalancerSecurityGroupId, profile?.BastionSecurityGroupId }
                                    .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                if (expectedSgIds.Count > 0)
                {
                    var resp = await ec2.DescribeSecurityGroupsAsync(new DescribeSecurityGroupsRequest { GroupIds = expectedSgIds }).ConfigureAwait(false);
                    sgs = resp.SecurityGroups != null && resp.SecurityGroups.Count == expectedSgIds.Count;
                }

                if (profile?.RouteTableIds != null && profile.RouteTableIds.Count > 0)
                {
                    var resp = await ec2.DescribeRouteTablesAsync(new DescribeRouteTablesRequest { RouteTableIds = profile.RouteTableIds.ToList() }).ConfigureAwait(false);
                    rts = resp.RouteTables != null && resp.RouteTables.Count == profile.RouteTableIds.Count;
                }

                if (profile?.NatGatewayIds != null && profile.NatGatewayIds.Count > 0)
                {
                    var resp = await ec2.DescribeNatGatewaysAsync(new DescribeNatGatewaysRequest { NatGatewayIds = profile.NatGatewayIds.ToList() }).ConfigureAwait(false);
                    nats = resp.NatGateways != null && resp.NatGateways.Count == profile.NatGatewayIds.Count;
                }

                if (!string.IsNullOrWhiteSpace(profile?.InternetGatewayId))
                {
                    var resp = await ec2.DescribeInternetGatewaysAsync(new DescribeInternetGatewaysRequest { InternetGatewayIds = new List<string> { profile.InternetGatewayId } }).ConfigureAwait(false);
                    igw = resp.InternetGateways != null && resp.InternetGateways.Count > 0;
                }
            }
            catch
            {
                // conservative: leave false if validation errors occur
            }

            return new EnsureVPCCheckResult(profile, vpcExists, pubSubnets, privSubnets, isoSubnets, sgs, rts, nats, igw);
        }

        /// <summary>
        /// EnsureVpcAsync: consults logs first; if profile found and VPC exists returns profile.
        /// Otherwise creates missing networking resources and writes a single ResourceRecord where
        /// ResourceRecord.Id contains serialized profile JSON.
        /// </summary>
        public static async Task<EnsureVPCProfile> EnsureVpcAsync(
            IAmazonEC2 ec2,
            Infralogger logger,
            string logicalNamePrefix,
            string vpcCidr = "10.20.0.0/16",
            IEnumerable<string>? publicCidrs = null,
            IEnumerable<string>? privateCidrs = null,
            IEnumerable<string>? isolatedCidrs = null,
            string callerCidr = "0.0.0.0/0",
            bool createNatGateways = true)
        {
            if (ec2 == null) throw new ArgumentNullException(nameof(ec2));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(logicalNamePrefix)) throw new ArgumentNullException(nameof(logicalNamePrefix));

            var check = await CheckLogsAsync(ec2, logger, logicalNamePrefix).ConfigureAwait(false);
            if (check.Profile != null && check.VpcExists)
            {
                return check.Profile;
            }

            publicCidrs ??= new[] { "10.20.1.0/24", "10.20.2.0/24" };
            privateCidrs ??= new[] { "10.20.11.0/24", "10.20.12.0/24" };
            isolatedCidrs ??= new[] { "10.20.21.0/24", "10.20.22.0/24" };

            var region = ec2.Config?.RegionEndpoint?.SystemName ?? "unknown";
            var vpcTag = $"{logicalNamePrefix}-vpc";

            // create or reuse VPC
            string vpcId;
            var found = await ec2.DescribeVpcsAsync(new DescribeVpcsRequest
            {
                Filters = new List<Filter> { new Filter("tag:Name", new List<string> { vpcTag }) }
            }).ConfigureAwait(false);

            if (found.Vpcs != null && found.Vpcs.Count > 0)
            {
                vpcId = found.Vpcs[0].VpcId;
            }
            else
            {
                var createVpc = await ec2.CreateVpcAsync(new CreateVpcRequest { CidrBlock = vpcCidr }).ConfigureAwait(false);
                vpcId = createVpc.Vpc.VpcId;
                await ec2.CreateTagsAsync(new CreateTagsRequest { Resources = new List<string> { vpcId }, Tags = new List<Tag> { new Tag("Name", vpcTag) } }).ConfigureAwait(false);
                await ec2.ModifyVpcAttributeAsync(new ModifyVpcAttributeRequest { VpcId = vpcId, EnableDnsHostnames = true }).ConfigureAwait(false);
            }

            var azs = (await ec2.DescribeAvailabilityZonesAsync()).AvailabilityZones.Take(2).Select(a => a.ZoneName).ToArray();
            if (azs.Length == 0) throw new InvalidOperationException("No availability zones detected in region");

            async Task<string> EnsureSubnetAsync(string cidr, string nameSuffix, string az, bool mapPublicIp)
            {
                try
                {
                    var existing = await ec2.DescribeSubnetsAsync(new DescribeSubnetsRequest
                    {
                        Filters = new List<Filter> {
                            new Filter("vpc-id", new List<string>{ vpcId }),
                            new Filter("cidr-block", new List<string>{ cidr })
                        }
                    }).ConfigureAwait(false);

                    if (existing.Subnets != null && existing.Subnets.Count > 0)
                    {
                        var sid = existing.Subnets[0].SubnetId;
                        await ec2.CreateTagsAsync(new CreateTagsRequest { Resources = new List<string> { sid }, Tags = new List<Tag> { new Tag("Name", $"{logicalNamePrefix}-{nameSuffix}") } }).ConfigureAwait(false);
                        return sid;
                    }
                }
                catch { }

                var resp = await ec2.CreateSubnetAsync(new CreateSubnetRequest { VpcId = vpcId, CidrBlock = cidr, AvailabilityZone = az }).ConfigureAwait(false);
                var subnetId = resp.Subnet.SubnetId;
                await ec2.CreateTagsAsync(new CreateTagsRequest { Resources = new List<string> { subnetId }, Tags = new List<Tag> { new Tag("Name", $"{logicalNamePrefix}-{nameSuffix}") } }).ConfigureAwait(false);

                if (mapPublicIp)
                    await ec2.ModifySubnetAttributeAsync(new ModifySubnetAttributeRequest { SubnetId = subnetId, MapPublicIpOnLaunch = true }).ConfigureAwait(false);

                return subnetId;
            }

            var pub = publicCidrs.ToArray();
            var pri = privateCidrs.ToArray();
            var iso = isolatedCidrs.ToArray();

            var publicSubnets = new List<string>
            {
                await EnsureSubnetAsync(pub[0], "public-a", azs[0], true).ConfigureAwait(false),
                await EnsureSubnetAsync(pub[1], "public-b", azs.Length > 1 ? azs[1] : azs[0], true).ConfigureAwait(false)
            };

            var privateSubnets = new List<string>
            {
                await EnsureSubnetAsync(pri[0], "private-a", azs[0], false).ConfigureAwait(false),
                await EnsureSubnetAsync(pri[1], "private-b", azs.Length > 1 ? azs[1] : azs[0], false).ConfigureAwait(false)
            };

            var isolatedSubnets = new List<string>
            {
                await EnsureSubnetAsync(iso[0], "isolated-a", azs[0], false).ConfigureAwait(false),
                await EnsureSubnetAsync(iso[1], "isolated-b", azs.Length > 1 ? azs[1] : azs[0], false).ConfigureAwait(false)
            };

            // internet gateway
            string? igwId = null;
            try
            {
                var igwResp = await ec2.DescribeInternetGatewaysAsync(new DescribeInternetGatewaysRequest
                {
                    Filters = new List<Filter> { new Filter("attachment.vpc-id", new List<string> { vpcId }) }
                }).ConfigureAwait(false);
                if (igwResp.InternetGateways != null && igwResp.InternetGateways.Count > 0) igwId = igwResp.InternetGateways[0].InternetGatewayId;
            }
            catch { }

            if (string.IsNullOrWhiteSpace(igwId))
            {
                var igwCreate = await ec2.CreateInternetGatewayAsync(new CreateInternetGatewayRequest()).ConfigureAwait(false);
                igwId = igwCreate.InternetGateway.InternetGatewayId;
                try { await ec2.AttachInternetGatewayAsync(new AttachInternetGatewayRequest { InternetGatewayId = igwId, VpcId = vpcId }).ConfigureAwait(false); } catch { }
            }

            // NAT gateways (best-effort)
            var natIds = new List<string>();
            if (createNatGateways)
            {
                for (int i = 0; i < publicSubnets.Count; i++)
                {
                    var subnetId = publicSubnets[i];
                    try
                    {
                        var existingNat = await ec2.DescribeNatGatewaysAsync(new DescribeNatGatewaysRequest { Filter = new List<Filter> { new Filter("subnet-id", new List<string> { subnetId }), new Filter("state", new List<string> { "available", "pending" }) } }).ConfigureAwait(false);
                        if (existingNat.NatGateways != null && existingNat.NatGateways.Count > 0)
                        {
                            natIds.Add(existingNat.NatGateways[0].NatGatewayId);
                            continue;
                        }
                    }
                    catch { }

                    try
                    {
                        var alloc = await ec2.AllocateAddressAsync(new AllocateAddressRequest { Domain = DomainType.Vpc }).ConfigureAwait(false);
                        var allocationId = alloc.AllocationId;
                        var natCreate = await ec2.CreateNatGatewayAsync(new CreateNatGatewayRequest { SubnetId = subnetId, AllocationId = allocationId }).ConfigureAwait(false);
                        var natId = natCreate.NatGateway.NatGatewayId;
                        natIds.Add(natId);
                    }
                    catch { }
                }
            }

            // route tables
            string publicRtId = await EnsureRouteTableAsync(ec2, vpcId, $"{logicalNamePrefix}-rt-public", igwId, null).ConfigureAwait(false);
            string privateRtId = await EnsureRouteTableAsync(ec2, vpcId, $"{logicalNamePrefix}-rt-private", null, natIds.FirstOrDefault()).ConfigureAwait(false);
            string isolatedRtId = await EnsureRouteTableAsync(ec2, vpcId, $"{logicalNamePrefix}-rt-isolated", null, null).ConfigureAwait(false);

            async Task AssociateIfMissing(string rtId, string subnetId)
            {
                try
                {
                    var desc = await ec2.DescribeRouteTablesAsync(new DescribeRouteTablesRequest { Filters = new List<Filter> { new Filter("association.subnet-id", new List<string> { subnetId }) } }).ConfigureAwait(false);
                    if (desc.RouteTables == null || desc.RouteTables.Count == 0)
                        await ec2.AssociateRouteTableAsync(new AssociateRouteTableRequest { RouteTableId = rtId, SubnetId = subnetId }).ConfigureAwait(false);
                }
                catch { }
            }

            foreach (var s in publicSubnets) await AssociateIfMissing(publicRtId, s).ConfigureAwait(false);
            foreach (var s in privateSubnets) await AssociateIfMissing(privateRtId, s).ConfigureAwait(false);
            foreach (var s in isolatedSubnets) await AssociateIfMissing(isolatedRtId, s).ConfigureAwait(false);

            // security groups
            string appSgId = await EnsureSecurityGroupAsync(ec2, vpcId, $"{logicalNamePrefix}-app-sg", "Application instances", new List<IpPermission>
            {
                new IpPermission { IpProtocol="tcp", FromPort=80, ToPort=80, Ipv4Ranges = new List<IpRange>{ new IpRange{ CidrIp="0.0.0.0/0" } } },
                new IpPermission { IpProtocol="tcp", FromPort=443, ToPort=443, Ipv4Ranges = new List<IpRange>{ new IpRange{ CidrIp="0.0.0.0/0" } } }
            }).ConfigureAwait(false);

            string albSgId = await EnsureSecurityGroupAsync(ec2, vpcId, $"{logicalNamePrefix}-alb-sg", "Load balancer", new List<IpPermission>
            {
                new IpPermission { IpProtocol="tcp", FromPort=80, ToPort=80, Ipv4Ranges = new List<IpRange>{ new IpRange{ CidrIp="0.0.0.0/0" } } },
                new IpPermission { IpProtocol="tcp", FromPort=443, ToPort=443, Ipv4Ranges = new List<IpRange>{ new IpRange{ CidrIp="0.0.0.0/0" } } }
            }).ConfigureAwait(false);

            try
            {
                var appDesc = await ec2.DescribeSecurityGroupsAsync(new DescribeSecurityGroupsRequest { GroupIds = new List<string> { appSgId } }).ConfigureAwait(false);
                var existsFromAlb = appDesc.SecurityGroups.First().IpPermissions.Any(p => p.UserIdGroupPairs != null && p.UserIdGroupPairs.Any(u => u.GroupId == albSgId));
                if (!existsFromAlb)
                {
                    await ec2.AuthorizeSecurityGroupIngressAsync(new AuthorizeSecurityGroupIngressRequest
                    {
                        GroupId = appSgId,
                        IpPermissions = new List<IpPermission> { new IpPermission { IpProtocol = "tcp", FromPort = 80, ToPort = 80, UserIdGroupPairs = new List<UserIdGroupPair> { new UserIdGroupPair { GroupId = albSgId } } } }
                    }).ConfigureAwait(false);
                }
            }
            catch { }

            string rdsSgId = await EnsureSecurityGroupAsync(ec2, vpcId, $"{logicalNamePrefix}-rds-sg", "RDS access", null).ConfigureAwait(false);

            string bastionSgId = await EnsureSecurityGroupAsync(ec2, vpcId, $"{logicalNamePrefix}-bastion-sg", "Bastion host", string.IsNullOrWhiteSpace(callerCidr) ? null : new List<IpPermission> { new IpPermission { IpProtocol = "tcp", FromPort = 22, ToPort = 22, Ipv4Ranges = new List<IpRange> { new IpRange { CidrIp = callerCidr } } } }).ConfigureAwait(false);

            try
            {
                var rdsPerms = new List<IpPermission>
                {
                    new IpPermission { IpProtocol="tcp", FromPort=5432, ToPort=5432, UserIdGroupPairs = new List<UserIdGroupPair>{ new UserIdGroupPair{ GroupId = appSgId } } },
                    new IpPermission { IpProtocol="tcp", FromPort=1433, ToPort=1433, UserIdGroupPairs = new List<UserIdGroupPair>{ new UserIdGroupPair{ GroupId = appSgId } } },
                    new IpPermission { IpProtocol="tcp", FromPort=5432, ToPort=5432, UserIdGroupPairs = new List<UserIdGroupPair>{ new UserIdGroupPair{ GroupId = bastionSgId } } },
                    new IpPermission { IpProtocol="tcp", FromPort=1433, ToPort=1433, UserIdGroupPairs = new List<UserIdGroupPair>{ new UserIdGroupPair{ GroupId = bastionSgId } } }
                };
                await ec2.AuthorizeSecurityGroupIngressAsync(new AuthorizeSecurityGroupIngressRequest { GroupId = rdsSgId, IpPermissions = rdsPerms }).ConfigureAwait(false);
            }
            catch { }

            var profileCreated = new EnsureVPCProfile(
                Region: region,
                VpcId: vpcId,
                PublicSubnetIds: publicSubnets,
                PrivateSubnetIds: privateSubnets,
                IsolatedSubnetIds: isolatedSubnets,
                AppSecurityGroupId: appSgId,
                RdsSecurityGroupId: rdsSgId,
                LoadBalancerSecurityGroupId: albSgId,
                BastionSecurityGroupId: bastionSgId,
                RouteTableIds: new List<string> { publicRtId, privateRtId, isolatedRtId },
                NatGatewayIds: natIds,
                InternetGatewayId: igwId
            );

            var serialized = JsonSerializer.Serialize(profileCreated);
            var record = new ResourceRecord
            {
                ResourceType = LogResourceType,
                Name = $"{logicalNamePrefix}-vpc-profile",
                Id = serialized,
                Region = region,
                CreatedAt = DateTime.UtcNow
            };

            await logger.AppendAsync(record).ConfigureAwait(false);
            return profileCreated;
        }

        /// <summary>
        /// CleanupVpcAsync: best-effort removal of resources described by the logged VpcProfile.
        /// The method reads the VpcProfile from the log (ResourceRecord.Id contains JSON), deserializes it and attempts to delete resources.
        /// Orchestrator should call dependent resource cleanup (RDS, etc.) before invoking this.
        /// </summary>
        public static async Task<bool> CleanupVpcAsync(
            IAmazonEC2 ec2,
            Infralogger logger,
            string logicalNamePrefix,
            int waitForNatDeleteMs = 5000)
        {
            if (ec2 == null) throw new ArgumentNullException(nameof(ec2));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(logicalNamePrefix)) throw new ArgumentNullException(nameof(logicalNamePrefix));

            var all = logger.ReadAll() ?? Enumerable.Empty<ResourceRecord>();
            var targetName = $"{logicalNamePrefix}-vpc-profile";
            var record = all.Reverse().FirstOrDefault(r =>
                string.Equals(r.ResourceType, LogResourceType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Name, targetName, StringComparison.OrdinalIgnoreCase));

            if (record == null || string.IsNullOrWhiteSpace(record.Id))
            {
                return true;
            }

            EnsureVPCProfile? profile;
            try
            {
                profile = JsonSerializer.Deserialize<EnsureVPCProfile>(record.Id);
            }
            catch
            {
                return false;
            }

            if (profile == null) return true;
            var vpcId = profile.VpcId;

            try
            {
                // 1) Delete NAT gateways
                foreach (var natId in profile.NatGatewayIds ?? Enumerable.Empty<string>())
                {
                    try { await ec2.DeleteNatGatewayAsync(new DeleteNatGatewayRequest { NatGatewayId = natId }).ConfigureAwait(false); } catch { }
                }

                await Task.Delay(waitForNatDeleteMs).ConfigureAwait(false);

                // 2) Delete route table associations and route tables (skip main)
                try
                {
                    var rtsResp = await ec2.DescribeRouteTablesAsync(new DescribeRouteTablesRequest { Filters = new List<Filter> { new Filter("vpc-id", new List<string> { vpcId }) } }).ConfigureAwait(false);
                    foreach (var rt in rtsResp.RouteTables)
                    {
                        var isMain = rt.Associations != null && rt.Associations.Any(a => a.Main == true);
                        if (isMain) continue;

                        if (rt.Associations != null)
                        {
                            foreach (var assoc in rt.Associations.Where(a => !string.IsNullOrWhiteSpace(a.RouteTableAssociationId)))
                            {
                                try { await ec2.DisassociateRouteTableAsync(new DisassociateRouteTableRequest { AssociationId = assoc.RouteTableAssociationId }).ConfigureAwait(false); } catch { }
                            }
                        }

                        try { await ec2.DeleteRouteTableAsync(new DeleteRouteTableRequest { RouteTableId = rt.RouteTableId }).ConfigureAwait(false); } catch { }
                    }
                }
                catch { }

                // 3) Release Elastic IPs (best-effort)
                try
                {
                    var addrs = await ec2.DescribeAddressesAsync(new DescribeAddressesRequest()).ConfigureAwait(false);
                    foreach (var a in addrs.Addresses)
                    {
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(a.AllocationId) && a.AssociationId == null && a.Domain == DomainType.Vpc)
                            {
                                await ec2.ReleaseAddressAsync(new ReleaseAddressRequest { AllocationId = a.AllocationId }).ConfigureAwait(false);
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                // 4) Delete subnets
                foreach (var sid in (profile.PublicSubnetIds ?? Enumerable.Empty<string>()).Concat(profile.PrivateSubnetIds ?? Enumerable.Empty<string>()).Concat(profile.IsolatedSubnetIds ?? Enumerable.Empty<string>()))
                {
                    try { await ec2.DeleteSubnetAsync(new DeleteSubnetRequest { SubnetId = sid }).ConfigureAwait(false); } catch { }
                }

                // 5) Delete security groups (ignore default)
                try
                {
                    var sgIds = new[] { profile.AppSecurityGroupId, profile.RdsSecurityGroupId, profile.LoadBalancerSecurityGroupId, profile.BastionSecurityGroupId }
                                .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

                    foreach (var sg in sgIds)
                    {
                        try { await ec2.DeleteSecurityGroupAsync(new DeleteSecurityGroupRequest { GroupId = sg }).ConfigureAwait(false); } catch { }
                    }
                }
                catch { }

                // 6) Detach & delete IGW
                if (!string.IsNullOrWhiteSpace(profile.InternetGatewayId))
                {
                    try { await ec2.DetachInternetGatewayAsync(new DetachInternetGatewayRequest { InternetGatewayId = profile.InternetGatewayId, VpcId = vpcId }).ConfigureAwait(false); } catch { }
                    try { await ec2.DeleteInternetGatewayAsync(new DeleteInternetGatewayRequest { InternetGatewayId = profile.InternetGatewayId }).ConfigureAwait(false); } catch { }
                }

                // 7) Delete VPC
                try { await ec2.DeleteVpcAsync(new DeleteVpcRequest { VpcId = vpcId }).ConfigureAwait(false); } catch { }

                return true;
            }
            catch
            {
                return false;
            }
        }

        #region helpers

        private static async Task<string> EnsureRouteTableAsync(IAmazonEC2 ec2, string vpcId, string nameTag, string? igwId, string? natGatewayId)
        {
            var find = await ec2.DescribeRouteTablesAsync(new DescribeRouteTablesRequest { Filters = new List<Filter> { new Filter("tag:Name", new List<string> { nameTag }), new Filter("vpc-id", new List<string> { vpcId }) } }).ConfigureAwait(false);
            if (find.RouteTables != null && find.RouteTables.Count > 0) return find.RouteTables[0].RouteTableId;

            var created = await ec2.CreateRouteTableAsync(new CreateRouteTableRequest { VpcId = vpcId }).ConfigureAwait(false);
            var rtId = created.RouteTable.RouteTableId;
            try { await ec2.CreateTagsAsync(new CreateTagsRequest { Resources = new List<string> { rtId }, Tags = new List<Tag> { new Tag("Name", nameTag) } }).ConfigureAwait(false); } catch { }

            if (!string.IsNullOrWhiteSpace(igwId))
            {
                try { await ec2.CreateRouteAsync(new CreateRouteRequest { RouteTableId = rtId, GatewayId = igwId, DestinationCidrBlock = "0.0.0.0/0" }).ConfigureAwait(false); } catch { }
            }
            else if (!string.IsNullOrWhiteSpace(natGatewayId))
            {
                try { await ec2.CreateRouteAsync(new CreateRouteRequest { RouteTableId = rtId, NatGatewayId = natGatewayId, DestinationCidrBlock = "0.0.0.0/0" }).ConfigureAwait(false); } catch { }
            }

            return rtId;
        }

        private static async Task<string> EnsureSecurityGroupAsync(IAmazonEC2 ec2, string vpcId, string name, string description, List<IpPermission>? ingress)
        {
            try
            {
                var find = await ec2.DescribeSecurityGroupsAsync(new DescribeSecurityGroupsRequest
                {
                    Filters = new List<Filter> {
                        new Filter("group-name", new List<string>{ name }),
                        new Filter("vpc-id", new List<string>{ vpcId })
                    }
                }).ConfigureAwait(false);

                if (find.SecurityGroups != null && find.SecurityGroups.Count > 0) return find.SecurityGroups[0].GroupId;
            }
            catch { }

            var create = await ec2.CreateSecurityGroupAsync(new CreateSecurityGroupRequest { GroupName = name, Description = description, VpcId = vpcId }).ConfigureAwait(false);
            var gid = create.GroupId;
            if (ingress != null && ingress.Count > 0)
            {
                try { await ec2.AuthorizeSecurityGroupIngressAsync(new AuthorizeSecurityGroupIngressRequest { GroupId = gid, IpPermissions = ingress }).ConfigureAwait(false); } catch { }
            }
            try { await ec2.CreateTagsAsync(new CreateTagsRequest { Resources = new List<string> { gid }, Tags = new List<Tag> { new Tag("Name", name) } }).ConfigureAwait(false); } catch { }
            return gid;
        }

        #endregion
    }
}
