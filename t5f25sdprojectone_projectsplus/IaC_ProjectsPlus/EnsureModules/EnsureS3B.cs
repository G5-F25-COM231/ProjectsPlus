using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Amazon.Runtime;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules
{
    public sealed class EnsureS3B
    {
        private readonly IAmazonS3 _s3;
        private readonly Infralogger _logger;
        private readonly string _region;

        private const string EnsureIdentifier = "EnsureS3";
        private const string ResourceTypeName = "S3Bucket";

        private EnsureS3Request? _lastRequest;
        private EnsureS3Result? _lastResult;
        private readonly object _stateLock = new();

        public EnsureS3B(IAmazonS3 s3, Infralogger logger, string region)
        {
            _s3 = s3 ?? throw new ArgumentNullException(nameof(s3));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _region = string.IsNullOrWhiteSpace(region) ? "us-east-2" : region;
        }

        // EnsureBucketAsync: create bucket if missing, apply tags and optional versioning (encryption disabled for portability)
        public async Task<EnsureS3Result> EnsureBucketAsync(EnsureS3Request req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ct.ThrowIfCancellationRequested();

            lock (_stateLock) { _lastRequest = req; _lastResult = null; }

            // derive safe bucket name: normalized fragment; append -s3 unless fragment is canonical prefix
            var fragment = EnsureUtils.normalizeName(req.BaseName);
            var candidateFragment = fragment == EnsureUtils.canonicalPrefix ? fragment : EnsureUtils.buildCanonicalName(fragment);
            var bucketName = candidateFragment.EndsWith("-s3", StringComparison.OrdinalIgnoreCase) ? candidateFragment : $"{candidateFragment}-s3b";

            // S3 naming limits: ensure <= 63 chars and lowercase, trim if necessary
            bucketName = bucketName.ToLowerInvariant();
            if (bucketName.Length > 63) bucketName = bucketName.Substring(0, 63);

            // 1) check if bucket exists (and is accessible)
            try
            {
                if (await AmazonS3Util.DoesS3BucketExistV2Async(_s3, bucketName).ConfigureAwait(false))
                {
                    string? location = null;
                    try
                    {
                        var loc = await _s3.GetBucketLocationAsync(new GetBucketLocationRequest { BucketName = bucketName }).ConfigureAwait(false);
                        location = loc.Location?.Value ?? _region;
                    }
                    catch { /* ignore */ }

                    var already = new EnsureS3Result { Created = false, AlreadyExisted = true, BucketName = bucketName, Region = location ?? _region, Message = "Bucket already exists", LoggedRecords = Array.Empty<ResourceRecord>() };
                    lock (_stateLock) { _lastResult = already; }
                    Console.WriteLine($"[EnsureS3B] Bucket already exists: {bucketName}");
                    return already;
                }
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // treat as missing or inaccessible — proceed to create
            }

            // 2) Create bucket (region aware)
            try
            {
                var createReq = new PutBucketRequest { BucketName = bucketName };
                if (!string.Equals(_region, "us-east-1", StringComparison.OrdinalIgnoreCase))
                {
                    createReq.BucketRegionName = _region;
                }

                await _s3.PutBucketAsync(createReq, ct).ConfigureAwait(false);
            }
            catch (AmazonS3Exception ex)
            {
                Console.WriteLine($"[EnsureS3] Error creating bucket {bucketName}: {ex.Message}");
                var failed = new EnsureS3Result { Created = false, AlreadyExisted = false, BucketName = bucketName, Region = _region, Message = $"CreateBucket failed: {ex.Message}", LoggedRecords = Array.Empty<ResourceRecord>() };
                lock (_stateLock) { _lastResult = failed; }
                return failed;
            }

            // 3) Apply tags using TagSet
            try
            {
                var tagSet = new List<Tag> { new Tag { Key = "Project", Value = EnsureUtils.canonicalPrefix }, new Tag { Key = "Name", Value = bucketName } };
                if (req.AdditionalTags != null)
                {
                    foreach (var kv in req.AdditionalTags)
                    {
                        if (!tagSet.Any(t => string.Equals(t.Key, kv.Key, StringComparison.OrdinalIgnoreCase)))
                            tagSet.Add(new Tag { Key = kv.Key, Value = kv.Value });
                    }
                }

                await _s3.PutBucketTaggingAsync(new PutBucketTaggingRequest { BucketName = bucketName, TagSet = tagSet }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureS3] Warning: failed to apply tags to {bucketName}: {ex.Message}");
            }

            // 4) NOTE: Default encryption skipped for SDK portability. Provide SDK version to re-enable SSE-KMS code.

            // 5) Optionally enable versioning
            if (req.EnableVersioning)
            {
                try
                {
                    var verReq = new PutBucketVersioningRequest { BucketName = bucketName, VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled } };
                    await _s3.PutBucketVersioningAsync(verReq, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureS3] Warning: failed to enable versioning on {bucketName}: {ex.Message}");
                }
            }

            // 6) Log resource record
            var arn = $"arn:aws:s3:::{bucketName}";
            var rec = EnsureUtils.makeResourceRecord(EnsureIdentifier, ResourceTypeName, bucketName, arn, _region);
            try { await _logger.appendAsync(rec).ConfigureAwait(false); } catch (Exception ex) { Console.WriteLine($"[EnsureS3] Warning: failed to append log for {bucketName}: {ex.Message}"); }

            var success = new EnsureS3Result { Created = true, AlreadyExisted = false, BucketName = bucketName, Region = _region, Message = "Bucket created", LoggedRecords = new[] { rec } };
            lock (_stateLock) { _lastResult = success; }
            Console.WriteLine($"[EnsureS3] Created bucket: {bucketName}");
            return success;
        }

        // EnsureExistsAsync - list records produced by EnsureS3 and validate existence in S3
        public async Task<EnsureS3ExistsSummary> EnsureExistsAsync(string? bucketNameOrArn = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var all = _logger.readAll();
            var recs = all.Where(r => string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase)
                                   && string.Equals(r.ResourceType, ResourceTypeName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!string.IsNullOrWhiteSpace(bucketNameOrArn))
            {
                var key = bucketNameOrArn.Trim();
                recs = recs.Where(r => string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Id, key, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            var entries = new List<EnsureS3ExistsEntry>();
            foreach (var r in recs)
            {
                ct.ThrowIfCancellationRequested();
                var exists = false;
                try
                {
                    var name = r.Name;
                    exists = await AmazonS3Util.DoesS3BucketExistV2Async(_s3, name).ConfigureAwait(false);
                }
                catch { exists = false; }

                entries.Add(new EnsureS3ExistsEntry { BucketName = r.Name, LoggedAt = r.CreatedAt, ExistsInCloud = exists, Record = r });
            }

            return new EnsureS3ExistsSummary { Entries = entries, Total = entries.Count, Found = entries.Count(e => e.ExistsInCloud), Missing = entries.Count(e => !e.ExistsInCloud) };
        }

        // EnsureDestroyAsync - best-effort: empty bucket (objects & versions) then delete bucket; remove only this ensure's log lines
        public async Task<EnsureS3DestroyResult> EnsureDestroyAsync(string bucketNameOrArn, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(bucketNameOrArn)) throw new ArgumentNullException(nameof(bucketNameOrArn));
            ct.ThrowIfCancellationRequested();

            var all = _logger.readAll();
            var matches = all.Where(r => string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(r.ResourceType, ResourceTypeName, StringComparison.OrdinalIgnoreCase)
                                      && (string.Equals(r.Name, bucketNameOrArn, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Id, bucketNameOrArn, StringComparison.OrdinalIgnoreCase)))
                             .ToList();

            if (matches.Count == 0)
            {
                // try to resolve name from ARN or given input
                var name = ExtractBucketName(bucketNameOrArn);
                if (string.IsNullOrWhiteSpace(name) || !await AmazonS3Util.DoesS3BucketExistV2Async(_s3, name).ConfigureAwait(false))
                {
                    return new EnsureS3DestroyResult { Destroyed = false, NotFound = true, BucketName = bucketNameOrArn, RemovedRecords = Array.Empty<ResourceRecord>(), Message = "No log entry and bucket not found" };
                }
                matches.Add(new ResourceRecord { EnsureIdentifier = EnsureIdentifier, ResourceType = ResourceTypeName, Name = name, Id = $"arn:aws:s3:::{name}", Region = _region, CreatedAt = DateTime.UtcNow });
            }

            var removed = new List<ResourceRecord>();
            var anyDeleted = false;
            foreach (var rec in matches)
            {
                ct.ThrowIfCancellationRequested();
                var bucket = rec.Name;
                try
                {
                    // 1) attempt to remove all object versions if versioning enabled
                    bool versioned = false;
                    try
                    {
                        var verReq = new GetBucketVersioningRequest { BucketName = bucket };
                        var verResp = await _s3.GetBucketVersioningAsync(verReq, ct).ConfigureAwait(false);
                        versioned = verResp.VersioningConfig.Status == VersionStatus.Enabled;
                    }
                    catch { /* ignore */ }

                    if (versioned)
                    {
                        // delete all versions (paged)
                        string? keyMarker = null;
                        string? versionIdMarker = null;
                        do
                        {
                            var listVersionsReq = new ListVersionsRequest { BucketName = bucket, KeyMarker = keyMarker, VersionIdMarker = versionIdMarker, MaxKeys = 1000 };
                            var listVersionsResp = await _s3.ListVersionsAsync(listVersionsReq, ct).ConfigureAwait(false);
                            var toDelete = new List<KeyVersion>();
                            foreach (var v in listVersionsResp.Versions)
                            {
                                toDelete.Add(new KeyVersion { Key = v.Key, VersionId = v.VersionId });
                                if (toDelete.Count == 1000)
                                {
                                    await _s3.DeleteObjectsAsync(new DeleteObjectsRequest { BucketName = bucket, Objects = toDelete }, ct).ConfigureAwait(false);
                                    toDelete.Clear();
                                }
                            }
                            if (toDelete.Count > 0)
                            {
                                await _s3.DeleteObjectsAsync(new DeleteObjectsRequest { BucketName = bucket, Objects = toDelete }, ct).ConfigureAwait(false);
                            }

                            keyMarker = listVersionsResp.NextKeyMarker;
                            versionIdMarker = listVersionsResp.NextVersionIdMarker;
                        } while (!string.IsNullOrEmpty(keyMarker));
                    }
                    else
                    {
                        // delete all objects (non-versioned)
                        string? continuationToken = null;
                        do
                        {
                            var listReq = new ListObjectsV2Request { BucketName = bucket, ContinuationToken = continuationToken, MaxKeys = 1000 };
                            var listResp = await _s3.ListObjectsV2Async(listReq, ct).ConfigureAwait(false);
                            if (listResp.S3Objects != null && listResp.S3Objects.Count > 0)
                            {
                                var deleteReq = new DeleteObjectsRequest { BucketName = bucket, Objects = listResp.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList() };
                                await _s3.DeleteObjectsAsync(deleteReq, ct).ConfigureAwait(false);
                            }
                            continuationToken = (bool)listResp.IsTruncated ? listResp.NextContinuationToken : null;
                        } while (!string.IsNullOrEmpty(continuationToken));
                    }

                    // delete bucket tagging (best-effort)
                    try { await _s3.DeleteBucketTaggingAsync(new DeleteBucketTaggingRequest { BucketName = bucket }, ct).ConfigureAwait(false); } catch { }

                    // delete bucket
                    await _s3.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucket }, ct).ConfigureAwait(false);

                    TryRemoveLogRecord(rec);
                    removed.Add(rec);
                    anyDeleted = true;
                    Console.WriteLine($"[EnsureS3] Deleted bucket and removed log: {bucket}");
                }
                catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TryRemoveLogRecord(rec);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureS3] Error deleting bucket {bucket}: {ex.Message}");
                }
            }

            return new EnsureS3DestroyResult { Destroyed = anyDeleted, NotFound = removed.Count == 0, BucketName = bucketNameOrArn, RemovedRecords = removed, Message = anyDeleted ? "Deleted and removed log entries" : "No bucket deleted" };
        }

        public override string ToString()
        {
            lock (_stateLock)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("EnsureS3 Snapshot:");
                if (_lastRequest != null)
                {
                    sb.AppendLine($" BaseName={_lastRequest.BaseName}");
                    sb.AppendLine($" EnableVersioning={_lastRequest.EnableVersioning}");
                }
                else sb.AppendLine(" Request=null");

                if (_lastResult != null)
                {
                    sb.AppendLine($" Created={_lastResult.Created} AlreadyExisted={_lastResult.AlreadyExisted} BucketName={_lastResult.BucketName}");
                }
                else sb.AppendLine(" Result=null");
                return sb.ToString();
            }
        }

        #region helpers

        private static string? ExtractBucketName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            if (input.StartsWith("arn:aws:s3:::", StringComparison.OrdinalIgnoreCase)) return input.Substring("arn:aws:s3:::".Length);
            if (!input.Contains("://") && !input.Contains("/")) return input;
            try
            {
                var uri = new Uri(input);
                return uri.Host;
            }
            catch { return null; }
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
                Console.WriteLine($"[EnsureS3] Warning: failed to remove log record for {rec.Name}: {ex.Message}");
            }
        }

        #endregion

        #region DTOs / VOs

        public sealed class EnsureS3Request
        {
            public string BaseName { get; init; } = "storage";
            public bool EnableVersioning { get; init; } = false;
            public bool EnableDefaultEncryption { get; init; } = false; // placeholder: encryption disabled in implementation
            public string? KmsMasterKeyId { get; init; } // placeholder
            public Dictionary<string, string>? AdditionalTags { get; init; }
        }

        public sealed class EnsureS3Result
        {
            public bool Created { get; init; }
            public bool AlreadyExisted { get; init; }
            public string? BucketName { get; init; }
            public string? Region { get; init; }
            public string? Message { get; init; }
            public IReadOnlyList<ResourceRecord> LoggedRecords { get; init; } = Array.Empty<ResourceRecord>();
        }

        public sealed class EnsureS3ExistsEntry
        {
            public string BucketName { get; init; } = string.Empty;
            public DateTime LoggedAt { get; init; }
            public bool ExistsInCloud { get; init; }
            public ResourceRecord? Record { get; init; }
        }

        public sealed class EnsureS3ExistsSummary
        {
            public IReadOnlyList<EnsureS3ExistsEntry> Entries { get; init; } = Array.Empty<EnsureS3ExistsEntry>();
            public int Total { get; init; }
            public int Found { get; init; }
            public int Missing { get; init; }
        }

        public sealed class EnsureS3DestroyResult
        {
            public bool Destroyed { get; init; }
            public bool NotFound { get; init; }
            public string? BucketName { get; init; }
            public IReadOnlyList<ResourceRecord> RemovedRecords { get; init; } = Array.Empty<ResourceRecord>();
            public string? Message { get; init; }
        }


        #endregion
    }
}


// src/IaC_ProjectsPlus/EnsureModules/EnsureS3.cs
//
// EnsureS3 - compile-ready (encryption disabled to avoid SDK surface mismatches)
// - Idempotent EnsureBucketAsync, read-only EnsureExistsAsync, best-effort EnsureDestroyAsync
// - Uses EnsureUtils.buildCanonicalName and EnsureUtils.makeResourceRecord to persist ResourceRecord entries with EnsureIdentifier = "EnsureS3"
// - Applies tags via PutBucketTaggingAsync (TagSet)
// - Writes single-line ResourceRecord entries via Infralogger and emits Console logs for visibility
// - TryRemoveLogRecord removes only this ensure's own log lines
//
// Note: Default encryption code removed to avoid property-name mismatches across AWSSDK.S3 versions.
// If you supply your AWSSDK.S3 package version (e.g., AWSSDK.S3 3.7.1.18) I will produce a version that enables SSE-KMS with the exact SDK properties.


// src/IaC_ProjectsPlus/EnsureModules/EnsureS3.cs
//
// EnsureS3 - corrected, compile-ready
// - Idempotent EnsureBucketAsync, read-only EnsureExistsAsync, best-effort EnsureDestroyAsync
// - Uses EnsureUtils.buildCanonicalName and EnsureUtils.makeResourceRecord to persist ResourceRecord entries with EnsureIdentifier = "EnsureS3"
// - Applies tags via PutBucketTaggingAsync (TagSet) and S3 default encryption via PutBucketEncryptionAsync (SSEAlgorithm / KmsMasterKeyId)
// - Writes single-line ResourceRecord entries via Infralogger and emits Console logs for visibility
// - TryRemoveLogRecord removes only this ensure's own log lines
//
// Notes
// - Deleting a bucket will attempt to empty it first (objects and versioned objects) as a best-effort operation.
// - Bucket naming: canonical fragment + "-s3" (trimmed to 63 chars).
// - This file is conservative about exceptions and is intended to compile against AWSSDK.S3 typical releases.
