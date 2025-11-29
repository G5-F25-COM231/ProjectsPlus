// src/IaC_ProjectsPlus/EnsureInitializer.cs
using System;
using Amazon;
using Amazon.Runtime;
using Amazon.IdentityManagement;
using Amazon.EC2;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.RDS;
using Amazon.ECS;
using Amazon.DynamoDBv2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules;
using Amazon.CloudWatchLogs;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus
{
    public sealed class EnsuresContainer
    {
        public EnsureIAM IAM { get; init; } = null!;
        public EnsureVPC VPC { get; init; } = null!;
        public EnsureS3 S3 { get; init; } = null!;
        public EnsureSM SM { get; init; } = null!;
        public EnsureRDS RDS { get; init; } = null!;
        public EnsureECS ECS { get; init; } = null!;
        public EnsureDDB DDB { get; init; } = null!;
    }

    public static class EnsureInitializer
    {
        public static EnsuresContainer CreateAll(AWSCredentials? creds = null, RegionEndpoint? region = null)
        {
            creds ??= CredsReader.ReadFromCsv();
            region ??= RegionEndpoint.USEast2;                   
           
            // AWS clients
            var iamClient = new AmazonIdentityManagementServiceClient(creds, region);
            var ec2Client = new AmazonEC2Client(creds, region);
            var s3Client = new AmazonS3Client(creds, region);
            var secretsClient = new AmazonSecretsManagerClient(creds, region);
            var rdsClient = new AmazonRDSClient(creds, region);
            var ecsClient = new AmazonECSClient(creds, region);
            var ddbClient = new AmazonDynamoDBClient(creds, region);
            var cwClient = new AmazonCloudWatchLogsClient(creds, region);

            // loggers
            var logger = new EnsureModules.Infralogger();
           

            // construct ensures — adjust constructors if your Ensure types differ
            var iam = new EnsureIAM(iamClient, logger, region.SystemName);
            var vpc = new EnsureVPC(ec2Client, logger, region.SystemName);
            var s3 = new EnsureS3(s3Client, logger, region.SystemName);
            var sm = new EnsureSM(secretsClient, logger, region.SystemName);
            var rds = new EnsureRDS(rdsClient, sm, logger, region.SystemName); // example: RDS may accept SecretsManager helper
            var ecs = new EnsureECS(ecsClient, cwClient, logger, region.SystemName);
            var ddb = new EnsureDDB(ddbClient, logger, region.SystemName);

            return new EnsuresContainer
            {
                IAM = iam,
                VPC = vpc,
                S3 = s3,
                SM = sm,
                RDS = rds,
                ECS = ecs,
                DDB = ddb
            };
        }
       
    }
}
