// src/IaC_ProjectsPlus/InfraContracts.cs
//
// Shared DTOs and summaries referenced by InfraOrchestrator and other modules.
// This corrected version exposes Steps as List<StepResult> so orchestration code can Add step results.

using System;
using System.Collections.Generic;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules
{
    // High-level requests passed into the orchestrator
    public sealed class InfraCreateRequest
    {
        // IAM
        public string IamExecutionRoleName { get; init; } = "ecs-exec-role";
        public string IamAssumeRolePolicyDocument { get; init; } = "{}";
        public string IamExecutionRoleArn { get; init; } = string.Empty;
        public string IamTaskRoleArn { get; init; } = string.Empty;

        // VPC
        public string VpcLogicalName { get; init; } = "projects-vpc";
        public string CallerCidr { get; init; } = "0.0.0.0/0";
        public bool CreateNatGateways { get; init; } = false;

        // S3
        public bool CreateS3 { get; init; } = true;
        public string S3BaseName { get; init; } = "projects-artifacts";
        public bool S3EnableVersioning { get; init; } = false;

        // Secrets Manager
        public bool CreateSecrets { get; init; } = true;
        public string SecretBaseName { get; init; } = "projects-db-secret";
        public string? InitialDbPassword { get; init; } = null;

        // RDS
        public bool CreateRds { get; init; } = true;
        public string RdsBaseName { get; init; } = "projects-db";
        public string RdsEngine { get; init; } = "sqlserver-ex";
        public string RdsInstanceClass { get; init; } = "db.t3.micro";
        public int RdsAllocatedStorageGb { get; init; } = 20;
        public string RdsMasterUsername { get; init; } = "mssqlexadmin";
        public string? RdsMasterUserPassword { get; init; } = "adminjdevnforne+";
        public bool RdsPubliclyAccessible { get; init; } = false;
        public bool RdsMultiAz { get; init; } = false;
        public string? RdsSubnetGroupName { get; init; } = null;
        public int RdsAvailabilityTimeoutSeconds { get; init; } = 900;

        // RDS/Secret rotation integration
        public string? SecretRotationLambdaArn { get; init; } = null;
        public int RotationAutomaticallyAfterDays { get; init; } = 30;

        // ECS
        public bool CreateEcs { get; init; } = true;
        public string EcsClusterLogicalName { get; init; } = "projects-ecs";
        public string TaskFamily { get; init; } = "projects-app";
        public string ContainerName { get; init; } = "app";
        public string ContainerImage { get; init; } = "nginx:latest";
        public int ContainerCpu { get; init; } = 256;
        public int ContainerMemory { get; init; } = 512;
        public bool CreateService { get; init; } = false;
        public string ServiceName { get; init; } = "projects-service";
        public int ServiceDesiredCount { get; init; } = 1;
        public IEnumerable<string>? EcsSubnetIds { get; init; }
        public IEnumerable<string>? EcsSecurityGroupIds { get; init; }
        public bool EcsAssignPublicIp { get; init; } = false;

        // DDB
        public bool CreateDdb { get; init; } = false;
        public string DdbBaseName { get; init; } = "projects-ddb";
        public bool DdbUseOnDemand { get; init; } = true;
    }

    public sealed class InfraDestroyRequest
    {
        public bool DeleteDdb { get; init; } = false;
        public string? DdbIdentifier { get; init; }
        public string? DdbBaseName { get; init; }

        public bool DeleteEcs { get; init; } = false;
        public string? EcsIdentifierOrName { get; init; }

        public bool DeleteRds { get; init; } = false;
        public string? RdsIdentifierOrName { get; init; }
        public bool SkipRdsFinalSnapshot { get; init; } = true;

        public bool DeleteSecrets { get; init; } = false;
        public string? SecretNameOrArn { get; init; }
        public bool ForceDeleteSecrets { get; init; } = false;
        public int SecretsRecoveryWindowDays { get; init; } = 7;

        public bool DeleteS3 { get; init; } = false;
        public string? S3BucketName { get; init; }

        public bool DeleteVpc { get; init; } = false;
        public string? VpcIdentifierOrName { get; init; }

        public bool DeleteIam { get; init; } = false;
        public string? IamIdentifierOrName { get; init; }
    }

    public sealed class InfraExistsRequest
    {
        public string? IamIdentifierOrName { get; init; }
        public string? VpcIdentifierOrName { get; init; }
        public string? S3BucketNameOrArn { get; init; }
        public string? SecretNameOrArn { get; init; }
        public string? RdsIdentifierOrName { get; init; }
        public string? EcsIdentifierOrName { get; init; }
        public string? DdbIdentifierOrName { get; init; }
    }

    // Orchestrator summary and step result types
    public sealed class InfraOrchestratorSummary
    {
        public string InvocationId { get; init; } = string.Empty;
        public DateTime StartedAt { get; init; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public long TotalDurationMs { get; set; }

        // Make Steps mutable so callers can Add step results
        public List<StepResult> Steps { get; init; } = new List<StepResult>();
    }

    public sealed class StepResult
    {
        public string StepName { get; init; } = string.Empty;
        public DateTime StartedAt { get; init; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public long DurationMs { get; set; }
        public bool Succeeded { get; set; }
        public object? Result { get; set; }
        public string? Error { get; set; }
    }

    public sealed class InfraOrchestratorExistsSummary
    {
        public string InvocationId { get; init; } = string.Empty;
        public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
        public List<InfraExistsEntry> Entries { get; init; } = new List<InfraExistsEntry>();
    }

    public sealed class InfraExistsEntry
    {
        public string Component { get; init; } = string.Empty;
        public object? Summary { get; init; }
        public string? Error { get; init; }
    }
}
