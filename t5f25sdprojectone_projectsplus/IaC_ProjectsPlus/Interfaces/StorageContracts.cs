// src/IaC_ProjectsPlus/StorageContracts.cs
//
// Service interfaces and supporting DTOs for DynamoDB and S3 CRUD operations.
// Place in namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus
{
    // ---------- DynamoDB (DDB) ----------

    /// <summary>
    /// Lightweight, provider-agnostic DynamoDB service contract for common CRUD and query operations.
    /// Implementations should map to Amazon.DynamoDBv2 or other providers as appropriate.
    /// Methods are cancellation-aware and designed for small-to-medium payloads; use streaming for very large items.
    /// </summary>
    public interface IDynamodbService
    {
        Task<DdbPutResult> PutItemAsync(DdbPutRequest req, CancellationToken ct = default);
        Task<DdbGetResult> GetItemAsync(DdbGetRequest req, CancellationToken ct = default);
        Task<DdbDeleteResult> DeleteItemAsync(DdbDeleteRequest req, CancellationToken ct = default);
        Task<DdbQueryResult> QueryAsync(DdbQueryRequest req, CancellationToken ct = default);
        Task<DdbScanResult> ScanAsync(DdbScanRequest req, CancellationToken ct = default);
        Task<DdbEnsureTableResult> EnsureTableAsync(DdbEnsureTableRequest req, CancellationToken ct = default);
        Task<DdbDestroyTableResult> DestroyTableAsync(DdbDestroyTableRequest req, CancellationToken ct = default);
        Task<DdbBatchWriteResult> BatchWriteAsync(DdbBatchWriteRequest req, CancellationToken ct = default);
    }

    // DDB DTOs (minimal, extend as needed)

    public sealed class DdbPutRequest
    {
        public string TableName { get; init; } = string.Empty;
        public IDictionary<string, object?> Item { get; init; } = new Dictionary<string, object?>();
        public bool OverwriteIfExists { get; init; } = true;
    }

    public sealed class DdbPutResult
    {
        public bool Success { get; init; }
        public string? Message { get; init; }
    }

    public sealed class DdbGetRequest
    {
        public string TableName { get; init; } = string.Empty;
        public IDictionary<string, object?> Key { get; init; } = new Dictionary<string, object?>();
        public bool ConsistentRead { get; init; } = false;
    }

    public sealed class DdbGetResult
    {
        public bool Found { get; init; }
        public IDictionary<string, object?>? Item { get; init; }
        public string? Message { get; init; }
    }

    public sealed class DdbDeleteRequest
    {
        public string TableName { get; init; } = string.Empty;
        public IDictionary<string, object?> Key { get; init; } = new Dictionary<string, object?>();
        public bool ReturnValues { get; init; } = false;
    }

    public sealed class DdbDeleteResult
    {
        public bool Deleted { get; init; }
        public IDictionary<string, object?>? OldItem { get; init; }
        public string? Message { get; init; }
    }

    public sealed class DdbQueryRequest
    {
        public string TableName { get; init; } = string.Empty;
        public string KeyConditionExpression { get; init; } = string.Empty;
        public IDictionary<string, object?> ExpressionAttributeValues { get; init; } = new Dictionary<string, object?>();
        public string? FilterExpression { get; init; }
        public int? Limit { get; init; }
        public string? ExclusiveStartKeyJson { get; init; } // optional continuation
    }

    public sealed class DdbQueryResult
    {
        public IReadOnlyList<IDictionary<string, object?>> Items { get; init; } = Array.Empty<IDictionary<string, object?>>();
        public string? LastEvaluatedKeyJson { get; init; }
        public long ScannedCount { get; init; }
    }

    public sealed class DdbScanRequest
    {
        public string TableName { get; init; } = string.Empty;
        public string? FilterExpression { get; init; }
        public IDictionary<string, object?> ExpressionAttributeValues { get; init; } = new Dictionary<string, object?>();
        public int? Limit { get; init; }
        public string? ExclusiveStartKeyJson { get; init; }
    }

    public sealed class DdbScanResult
    {
        public IReadOnlyList<IDictionary<string, object?>> Items { get; init; } = Array.Empty<IDictionary<string, object?>>();
        public string? LastEvaluatedKeyJson { get; init; }
        public long ScannedCount { get; init; }
    }

    public sealed class DdbEnsureTableRequest
    {
        public string TableName { get; init; } = string.Empty;
        public string PartitionKeyName { get; init; } = "pk";
        public string SortKeyName { get; init; } = string.Empty; // empty => no sort key
        public long ReadCapacityUnits { get; init; } = 5;
        public long WriteCapacityUnits { get; init; } = 5;
        public bool OnDemandBilling { get; init; } = true;
        public IReadOnlyDictionary<string, string>? AdditionalGlobalSecondaryIndexes { get; init; } = null;
    }

    public sealed class DdbEnsureTableResult
    {
        public bool Created { get; init; }
        public bool AlreadyExisted { get; init; }
        public string? TableArn { get; init; }
        public string? Message { get; init; }
    }

    public sealed class DdbDestroyTableRequest
    {
        public string TableName { get; init; } = string.Empty;
        public bool SkipIfNotFound { get; init; } = true;
    }

    public sealed class DdbDestroyTableResult
    {
        public bool Destroyed { get; init; }
        public bool NotFound { get; init; }
        public string? Message { get; init; }
    }

    public sealed class DdbBatchWriteRequest
    {
        public string TableName { get; init; } = string.Empty;
        public IReadOnlyList<IDictionary<string, object?>> ItemsToPut { get; init; } = Array.Empty<IDictionary<string, object?>>();
        public IReadOnlyList<IDictionary<string, object?>> KeysToDelete { get; init; } = Array.Empty<IDictionary<string, object?>>();
    }

    public sealed class DdbBatchWriteResult
    {
        public int PutCount { get; init; }
        public int DeleteCount { get; init; }
        public IReadOnlyList<IDictionary<string, object?>>? UnprocessedItems { get; init; }
        public string? Message { get; init; }
    }

    // ---------- S3 Bucket service ----------

    /// <summary>
    /// Provider-agnostic S3 bucket service contract for object CRUD and bucket lifecycle operations.
    /// Implementations should map to AWSSDK.S3 or an S3-compatible provider.
    /// </summary>
    public interface IS3BucketService
    {
        Task<S3EnsureBucketResult> EnsureBucketAsync(S3EnsureBucketRequest req, CancellationToken ct = default);
        Task<S3PutObjectResult> PutObjectAsync(S3PutObjectRequest req, CancellationToken ct = default);
        Task<S3GetObjectResult> GetObjectAsync(S3GetObjectRequest req, CancellationToken ct = default);
        Task<S3DeleteObjectResult> DeleteObjectAsync(S3DeleteObjectRequest req, CancellationToken ct = default);
        Task<S3ListObjectsResult> ListObjectsAsync(S3ListObjectsRequest req, CancellationToken ct = default);
        Task<S3DestroyBucketResult> DestroyBucketAsync(S3DestroyBucketRequest req, CancellationToken ct = default);
    }

    // S3 DTOs (minimal)

    public sealed class S3EnsureBucketRequest
    {
        public string BucketName { get; init; } = string.Empty;
        public string? Region { get; init; } = null;
        public bool EnableVersioning { get; init; } = false;
        public bool PublicReadAccess { get; init; } = false;
        public IReadOnlyDictionary<string, string>? Tags { get; init; }
    }

    public sealed class S3EnsureBucketResult
    {
        public bool Created { get; init; }
        public bool AlreadyExisted { get; init; }
        public string? BucketArn { get; init; }
        public string? Message { get; init; }
    }

    public sealed class S3PutObjectRequest
    {
        public string BucketName { get; init; } = string.Empty;
        public string Key { get; init; } = string.Empty;
        public Stream Content { get; init; } = Stream.Null;
        public string? ContentType { get; init; }
        public long? ContentLength { get; init; }
        public IReadOnlyDictionary<string, string>? Metadata { get; init; }
        public bool Overwrite { get; init; } = true;
    }

    public sealed class S3PutObjectResult
    {
        public bool Success { get; init; }
        public string? ETag { get; init; }
        public string? Location { get; init; }
        public string? Message { get; init; }
    }

    public sealed class S3GetObjectRequest
    {
        public string BucketName { get; init; } = string.Empty;
        public string Key { get; init; } = string.Empty;
        public bool ReturnStream { get; init; } = true;
    }

    public sealed class S3GetObjectResult
    {
        public bool Found { get; init; }
        public Stream? ContentStream { get; init; }
        public string? ContentType { get; init; }
        public long? ContentLength { get; init; }
        public IReadOnlyDictionary<string, string>? Metadata { get; init; }
        public string? Message { get; init; }
    }

    public sealed class S3DeleteObjectRequest
    {
        public string BucketName { get; init; } = string.Empty;
        public string Key { get; init; } = string.Empty;
        public bool SkipIfNotFound { get; init; } = true;
    }

    public sealed class S3DeleteObjectResult
    {
        public bool Deleted { get; init; }
        public bool NotFound { get; init; }
        public string? Message { get; init; }
    }

    public sealed class S3ListObjectsRequest
    {
        public string BucketName { get; init; } = string.Empty;
        public string? Prefix { get; init; }
        public string? ContinuationToken { get; init; }
        public int? MaxKeys { get; init; }
    }

    public sealed class S3ListObjectsResult
    {
        public IReadOnlyList<S3ObjectDescriptor> Objects { get; init; } = Array.Empty<S3ObjectDescriptor>();
        public string? NextContinuationToken { get; init; }
    }

    public sealed class S3ObjectDescriptor
    {
        public string Key { get; init; } = string.Empty;
        public long Size { get; init; }
        public DateTime LastModifiedUtc { get; init; }
        public string? ETag { get; init; }
        public string? ContentType { get; init; }
    }

    public sealed class S3DestroyBucketRequest
    {
        public string BucketName { get; init; } = string.Empty;
        public bool ForceDeleteObjects { get; init; } = true;
        public bool SkipIfNotFound { get; init; } = true;
    }

    public sealed class S3DestroyBucketResult
    {
        public bool Destroyed { get; init; }
        public bool NotFound { get; init; }
        public int RemovedObjectCount { get; init; }
        public string? Message { get; init; }
    }
}
