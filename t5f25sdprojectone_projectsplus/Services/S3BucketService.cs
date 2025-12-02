using Amazon;
using Amazon.Runtime.Internal.Endpoints.StandardLibrary;
using Amazon.S3;
using Amazon.S3.Model;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.Interfaces;
using static t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules.EnsureS3B;

namespace t5f25sdprojectone_projectsplus.Services
{
    public sealed class S3BucketServiceOptions
    {
        public string BucketName { get; init; } = string.Empty;
        public string? Region { get; init; } = null;
        public bool EnableVersioning { get; init; } = false;
        public bool PublicReadAccess { get; init; } = false;
        public IReadOnlyDictionary<string, string>? Tags { get; init; }
    }

    public class S3BucketService : IS3BucketService, IDisposable
    {
        private readonly IAmazonS3 _client;
        private readonly S3BucketServiceOptions _options;

        private bool _disposed;

        public S3BucketService(IAmazonS3 client, EnsureS3Result s3Infra)
        {
            var reg = RegionEndpoint.USEast2;
            
            S3BucketServiceOptions options = new()
            {
                BucketName = s3Infra.BucketName,
                Region = (reg).SystemName
            };

            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(_options.BucketName))
                throw new ArgumentException("BucketName must be provided in options.", nameof(options));
        }

        public S3BucketServiceOptions Options => _options;
            

        // Helper to check bucket existence without DoesS3BucketExistV2Async
        private async Task<bool> BucketExistsAsync(string bucketName, CancellationToken ct)
        {
            // Try HeadBucket (preferred) and fall back to ListBuckets
            try
            {
                var headReq = new GetACLRequest { BucketName = bucketName };
                await _client.GetACLAsync(headReq, ct).ConfigureAwait(false);
                return true;
            }
            catch (AmazonS3Exception s3ex) when (s3ex.StatusCode == System.Net.HttpStatusCode.NotFound || s3ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return false;
            }
            catch
            {
                // Fallback: list buckets and check name
                try
                {
                    var list = await _client.ListBucketsAsync(ct).ConfigureAwait(false);
                    return list.Buckets.Any(b => string.Equals(b.BucketName, bucketName, StringComparison.Ordinal));
                }
                catch
                {
                    return false;
                }
            }
        }

        public async Task<S3EnsureBucketResult> EnsureBucketAsync(S3EnsureBucketRequest req, CancellationToken ct = default)
        {
            var bucketName = string.IsNullOrWhiteSpace(req.BucketName) ? _options.BucketName : req.BucketName;
            var region = req.Region ?? _options.Region;
            var enableVersioning = req.EnableVersioning || _options.EnableVersioning;
            var publicRead = req.PublicReadAccess || _options.PublicReadAccess;
            var tags = req.Tags ?? _options.Tags;

            try
            {
                var exists = await BucketExistsAsync(bucketName, ct).ConfigureAwait(false);
                if (exists)
                {
                    return new S3EnsureBucketResult { Created = false, AlreadyExisted = true };
                }

                var createReq = new PutBucketRequest { BucketName = bucketName };
                await _client.PutBucketAsync(createReq, ct).ConfigureAwait(false);

                if (enableVersioning)
                {
                    var versionReq = new PutBucketVersioningRequest
                    {
                        BucketName = bucketName,
                        VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled }
                    };
                    await _client.PutBucketVersioningAsync(versionReq, ct).ConfigureAwait(false);
                }

                if (tags != null && tags.Count > 0)
                {
                    var tagSet = tags.Select(kv => new Tag { Key = kv.Key, Value = kv.Value }).ToList();
                    var tagReq = new PutBucketTaggingRequest { BucketName = bucketName, TagSet = tagSet };
                    await _client.PutBucketTaggingAsync(tagReq, ct).ConfigureAwait(false);
                }

                if (publicRead)
                {
                    var policy = $"{{\"Version\":\"2012-10-17\",\"Statement\":[{{\"Sid\":\"PublicReadGetObject\",\"Effect\":\"Allow\",\"Principal\":\"*\",\"Action\":[\"s3:GetObject\"],\"Resource\":[\"arn:aws:s3:::{bucketName}/*\"]}}]}}";
                    await _client.PutBucketPolicyAsync(new PutBucketPolicyRequest { BucketName = bucketName, Policy = policy }, ct).ConfigureAwait(false);
                }

                return new S3EnsureBucketResult { Created = true, AlreadyExisted = false, BucketArn = $"arn:aws:s3:::{bucketName}" };
            }
            catch (Exception ex)
            {
                return new S3EnsureBucketResult { Created = false, Message = ex.Message };
            }
        }


        public async Task<S3PutObjectResult> PutObjectAsync(S3PutObjectRequest req, CancellationToken ct = default)
        {
            var bucket = string.IsNullOrWhiteSpace(req.BucketName) ? _options.BucketName : req.BucketName;

            try
            {
                var putReq = new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = req.Key,
                    InputStream = req.Content ?? Stream.Null,
                    ContentType = req.ContentType,
                };

                if (req.Metadata != null)
                {
                    foreach (var kv in req.Metadata)
                        putReq.Metadata.Add(kv.Key, kv.Value);
                }

                // Upload
                var putResp = await _client.PutObjectAsync(putReq, ct).ConfigureAwait(false);

                // Fetch metadata (lightweight)
                var metaResp = await _client.GetObjectMetadataAsync(bucket, req.Key, ct).ConfigureAwait(false);

                // Build ARN (S3 does not return ARN; construct it)
                var arn = $"arn:aws:s3:::{bucket}/{req.Key}";

                // Create presigned URL (signed by AWS credentials). Adjust expiry as needed.
                var presignRequest = new GetPreSignedUrlRequest
                {
                    BucketName = bucket,
                    Key = req.Key,
                    Expires = DateTime.UtcNow.AddMinutes(15),
                    Verb = HttpVerb.GET
                };
                var presignedUrl = _client.GetPreSignedURL(presignRequest);

                // Collect metadata dictionary (if any)
                IDictionary<string, string> returnedMetadata = null;
                if (metaResp.Metadata != null && metaResp.Metadata.Keys.Count > 0)
                {
                    returnedMetadata = metaResp.Metadata.Keys
                        .Cast<string>()
                        .ToDictionary(k => k, k => metaResp.Metadata[k]);
                }

                var objectInfo = new ObjectInfo
                {
                    Bucket = bucket,
                    Key = req.Key,
                    Arn = arn,
                    AwsUrl = presignedUrl,
                    Location = $"s3://{bucket}/{req.Key}",
                    ETag = putResp.ETag,
                    LastModified = metaResp.LastModified,
                    Size = metaResp.ContentLength,
                    ContentType = metaResp.Headers.ContentType,
                    StorageClass = metaResp.Headers["x-amz-storage-class"],
                    Metadata = returnedMetadata
                };

                return new S3PutObjectResult
                {
                    Success = true,
                    ETag = putResp.ETag,
                    Location = objectInfo.Location,
                    ObjectInfo = objectInfo
                };
            }
            catch (Exception ex)
            {
                return new S3PutObjectResult { Success = false, Message = ex.Message };
            }
        }


        public async Task<S3GetObjectResult> GetObjectAsync(S3GetObjectRequest req, CancellationToken ct = default)
        {
            var bucket = string.IsNullOrWhiteSpace(req.BucketName) ? _options.BucketName : req.BucketName;
            try
            {
                var getReq = new GetObjectRequest { BucketName = bucket, Key = req.Key };
                var resp = await _client.GetObjectAsync(getReq, ct).ConfigureAwait(false);

                Stream? contentStream = null;
                if (req.ReturnStream)
                {
                    var mem = new MemoryStream();
                    await resp.ResponseStream.CopyToAsync(mem, ct).ConfigureAwait(false);
                    mem.Position = 0;
                    contentStream = mem;
                }

                // resp.LastModified is DateTime? in some SDKs; handle nullable safely
                DateTime? lastModifiedUtc = resp.LastModified.HasValue ? resp.LastModified.Value.ToUniversalTime() : null;

               
                return new S3GetObjectResult
                {
                    Found = true,
                    ContentStream = contentStream,
                    ContentType = resp.Headers.ContentType,
                    ContentLength = resp.Headers.ContentLength,
                    Metadata = resp.Metadata.Keys.Cast<string>().ToDictionary(k => k, k => resp.Metadata[k]),
                    Message = lastModifiedUtc?.ToString("o")
                };
            }
            catch (AmazonS3Exception s3ex) when (s3ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new S3GetObjectResult { Found = false };
            }
            catch (Exception ex)
            {
                return new S3GetObjectResult { Found = false, Message = ex.Message };
            }
        }

        public async Task<S3DeleteObjectResult> DeleteObjectAsync(S3DeleteObjectRequest req, CancellationToken ct = default)
        {
            var bucket = string.IsNullOrWhiteSpace(req.BucketName) ? _options.BucketName : req.BucketName;
            try
            {
                var deleteReq = new DeleteObjectRequest { BucketName = bucket, Key = req.Key };
                await _client.DeleteObjectAsync(deleteReq, ct).ConfigureAwait(false);
                return new S3DeleteObjectResult { Deleted = true, NotFound = false };
            }
            catch (AmazonS3Exception s3ex) when (s3ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new S3DeleteObjectResult { Deleted = false, NotFound = true };
            }
            catch (Exception ex)
            {
                return new S3DeleteObjectResult { Deleted = false, Message = ex.Message };
            }
        }

        public async Task<S3ListObjectsResult> ListObjectsAsync(S3ListObjectsRequest req, CancellationToken ct = default)
        {
            var bucket = string.IsNullOrWhiteSpace(req.BucketName) ? _options.BucketName : req.BucketName;
            try
            {
                var listReq = new ListObjectsV2Request
                {
                    BucketName = bucket,
                    Prefix = req.Prefix,
                    ContinuationToken = req.ContinuationToken,
                    MaxKeys = req.MaxKeys ?? 1000
                };

                var resp = await _client.ListObjectsV2Async(listReq, ct).ConfigureAwait(false);

                var objects = resp.S3Objects.Select(o => new S3ObjectDescriptor
                {
                    Key = o.Key,
                    Size = (long)o.Size,
                    LastModifiedUtc = o.LastModified.HasValue ? o.LastModified.Value.ToUniversalTime() : DateTime.MinValue,
                    ETag = o.ETag,
                    ContentType = null
                }).ToList();

                return new S3ListObjectsResult
                {
                    Objects = objects,
                    NextContinuationToken = resp.NextContinuationToken
                };
            }
            catch (Exception)
            {
                return new S3ListObjectsResult { Objects = Array.Empty<S3ObjectDescriptor>(), NextContinuationToken = null };
            }
        }

        public async Task<S3DestroyBucketResult> DestroyBucketAsync(S3DestroyBucketRequest req, CancellationToken ct = default)
        {
            var bucket = string.IsNullOrWhiteSpace(req.BucketName) ? _options.BucketName : req.BucketName;
            try
            {
                var exists = await BucketExistsAsync(bucket, ct).ConfigureAwait(false);
                if (!exists)
                {
                    return new S3DestroyBucketResult { Destroyed = false, NotFound = true };
                }

                int removed = 0;
                if (req.ForceDeleteObjects)
                {
                    string? continuation = null;
                    do
                    {
                        var listReq = new ListObjectsV2Request { BucketName = bucket, ContinuationToken = continuation, MaxKeys = 1000 };
                        var listResp = await _client.ListObjectsV2Async(listReq, ct).ConfigureAwait(false);
                        if (listResp.S3Objects.Count > 0)
                        {
                            var deleteObjectsRequest = new DeleteObjectsRequest { BucketName = bucket };
                            deleteObjectsRequest.Objects.AddRange(listResp.S3Objects.Select(o => new KeyVersion { Key = o.Key }));
                            var delResp = await _client.DeleteObjectsAsync(deleteObjectsRequest, ct).ConfigureAwait(false);
                            removed += delResp.DeletedObjects.Count;
                        }
                        continuation = listResp.NextContinuationToken;
                    } while (continuation != null);
                }

                await _client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucket }, ct).ConfigureAwait(false);

                return new S3DestroyBucketResult { Destroyed = true, NotFound = false, RemovedObjectCount = removed };
            }
            catch (Exception ex)
            {
                return new S3DestroyBucketResult { Destroyed = false, Message = ex.Message };
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _client?.Dispose();
                _disposed = true;
            }
        }

        
    }
}
