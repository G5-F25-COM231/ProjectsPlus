// src/ProjectsPlus.Comms/Services/AttachmentService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.ECS.Model;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.Interfaces;
using t5f25sdprojectone_projectsplus.Models.Communication;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    /// <summary>
    /// AttachmentService using the StorageContracts interfaces (IS3BucketService, IDynamodbService).
    /// - Uses S3PutObjectRequest / S3GetObjectRequest / S3DeleteObjectRequest
    /// - Uses DdbPutRequest / DdbGetRequest / DdbDeleteRequest
    /// - Persists pointer records in SQL Server via ProjectsPlusDbContext.MessageAttachments
    /// </summary>
    public class AttachmentService : IAttachmentService
    {
        private readonly ProjectsPlusDbContext _db;
        private readonly IS3BucketService _s3;
        private readonly IDynamodbService _ddb;
        private readonly string _bucketName;
        private readonly string _ddbTableName;        
        public AttachmentService(
            IServiceProvider svc,
            IS3BucketService s3BucketService,
            IDynamodbService dynamoDbService)
        {            
            using var scope = svc.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ProjectsPlusDbContext>();
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _s3 = s3BucketService ?? throw new ArgumentNullException(nameof(s3BucketService));
            _ddb = dynamoDbService ?? throw new ArgumentNullException(nameof(dynamoDbService));

            // Read configured names from the concrete services' options (StorageContracts expose GetOptions())
            var s3Opts = _s3.Options ?? (_s3 as dynamic)?.Options;
            _bucketName = !string.IsNullOrWhiteSpace(s3Opts?.BucketName) ? s3Opts.BucketName : "projectsplus-attachments";

            var ddbOpts = _ddb.Options ?? (_ddb as dynamic)?.Options;
            _ddbTableName = !string.IsNullOrWhiteSpace(ddbOpts?.TableName) ? ddbOpts.TableName : "ProjectsPlusAttachments";
        }

        public async Task<AttachmentDescriptor> UploadAsync(Stream content, string filename, string contentType, Guid uploaderUserId, Guid? messageId = null, CancellationToken ct = default)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (string.IsNullOrWhiteSpace(filename)) throw new ArgumentNullException(nameof(filename));

            var attachmentId = Guid.NewGuid();
            var key = $"{attachmentId:N}/{filename}";

            // 1) Upload to S3 via S3PutObjectRequest
            string storagePointer;
            S3PutObjectResult putResult;
            try
            {
                var putReq = new S3PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = key,
                    Content = content,
                    ContentType = contentType,
                    ContentLength = content.CanSeek ? content.Length : null
                };

                putResult = await _s3.PutObjectAsync(putReq, ct).ConfigureAwait(false);

                storagePointer = !string.IsNullOrWhiteSpace(putResult?.Location)
                    ? putResult.Location
                    : putResult?.ObjectInfo?.Location ?? $"s3://{_bucketName}/{key}";
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to upload attachment to S3", ex);
            }

            // 2) Write metadata to DynamoDB (best-effort) using DdbPutRequest
            DdbPutResult ddbPutResult = new DdbPutResult { Success = false };
            try
            {
                var item = new Dictionary<string, object?>
                {
                    ["AttachmentId"] = attachmentId.ToString("D"),
                    ["Filename"] = filename,
                    ["ContentType"] = contentType,
                    ["StoragePointer"] = storagePointer,
                    ["UploaderUserId"] = uploaderUserId.ToString("D"),
                    ["MessageId"] = messageId?.ToString("D"),
                    ["CreatedAt"] = DateTime.UtcNow.ToString("o")
                };

                var ddbReq = new DdbPutRequest
                {
                    TableName = _ddbTableName,
                    Item = item,
                    OverwriteIfExists = true
                };

                ddbPutResult = await _ddb.PutItemAsync(ddbReq, ct).ConfigureAwait(false);
            }
            catch
            {
                // ignore ddb failures (best-effort)
                ddbPutResult = new DdbPutResult { Success = false };
            }

            // 3) Persist pointer in SQL Server
            var entity = new MessageAttachmentEntity
            {
                AttachmentId = attachmentId,
                MessageId = messageId ?? Guid.Empty,
                Filename = filename,
                ContentType = contentType,
                StoragePointer = storagePointer,
                // store the attachmentId as DdbId when DDB write succeeded (Ddb contract doesn't return an id)
                DdbId = ddbPutResult != null && ddbPutResult.Success ? attachmentId.ToString("D") : null,
                SizeBytes = content.CanSeek ? content.Length : null,
                UploaderUserId = uploaderUserId,
                CreatedAt = DateTime.UtcNow
            };

            await _db.MessageAttachments!.AddAsync(entity, ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            return new AttachmentDescriptor
            {
                AttachmentId = attachmentId,
                MessageId = messageId,
                Filename = filename,
                ContentType = contentType,
                SizeBytes = entity.SizeBytes,
                StoragePointer = storagePointer,
                DdbId = entity.DdbId,
                UploaderUserId = entity.UploaderUserId ?? (Guid?)null,
                CreatedAt = entity.CreatedAt
            };
        }

        public async Task<Stream?> DownloadAsync(Guid attachmentId, CancellationToken ct = default)
        {
            var e = await _db.MessageAttachments!.AsNoTracking().FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, ct).ConfigureAwait(false);
            if (e == null || string.IsNullOrWhiteSpace(e.StoragePointer)) return null;

            try
            {
                var key = ExtractKeyFromStoragePointer(e.StoragePointer, _bucketName);

                var getReq = new S3GetObjectRequest
                {
                    BucketName = _bucketName,
                    Key = key,
                    ReturnStream = true
                };

                var getResult = await _s3.GetObjectAsync(getReq, ct).ConfigureAwait(false);
                if (getResult == null || !getResult.Found) return null;

                return getResult.ContentStream;
            }
            catch
            {
                return null;
            }
        }

        public async Task<AttachmentDescriptor?> GetMetadataAsync(Guid attachmentId, CancellationToken ct = default)
        {
            var e = await _db.MessageAttachments!.AsNoTracking().FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, ct).ConfigureAwait(false);
            if (e == null) return null;

            // Optionally fetch DynamoDB metadata (best-effort)
            if (!string.IsNullOrWhiteSpace(e.DdbId))
            {
                try
                {
                    var key = new Dictionary<string, object?> { ["pk"] = e.DdbId };
                    var getReq = new DdbGetRequest
                    {
                        TableName = _ddbTableName,
                        Key = key,
                        ConsistentRead = true
                    };

                    var getResult = await _ddb.GetItemAsync(getReq, ct).ConfigureAwait(false);
                    // getResult.Item can be used/merged if desired
                }
                catch
                {
                    // ignore
                }
            }

            return new AttachmentDescriptor
            {
                AttachmentId = e.AttachmentId,
                MessageId = e.MessageId == Guid.Empty ? null : e.MessageId,
                Filename = e.Filename ?? string.Empty,
                ContentType = e.ContentType,
                SizeBytes = e.SizeBytes,
                StoragePointer = e.StoragePointer ?? string.Empty,
                DdbId = e.DdbId,
                UploaderUserId = e.UploaderUserId ?? (Guid?)null,
                CreatedAt = e.CreatedAt
            };
        }

        public async Task<bool> DeleteAsync(Guid attachmentId, CancellationToken ct = default)
        {
            var e = await _db.MessageAttachments!.FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, ct).ConfigureAwait(false);
            if (e == null) return false;

            var success = true;

            // Delete from S3
            try
            {
                var key = ExtractKeyFromStoragePointer(e.StoragePointer, _bucketName);
                var delReq = new S3DeleteObjectRequest
                {
                    BucketName = _bucketName,
                    Key = key,
                    SkipIfNotFound = true
                };

                var delResult = await _s3.DeleteObjectAsync(delReq, ct).ConfigureAwait(false);
                if (delResult == null || !delResult.Deleted && !delResult.NotFound)
                    success = false;
            }
            catch
            {
                success = false;
            }

            // Delete from DynamoDB (best-effort)
            if (!string.IsNullOrWhiteSpace(e.DdbId))
            {
                try
                {
                    var key = new Dictionary<string, object?> { ["pk"] = e.DdbId };
                    var delReq = new DdbDeleteRequest
                    {
                        TableName = _ddbTableName,
                        Key = key,
                        ReturnValues = false
                    };

                    var delResult = await _ddb.DeleteItemAsync(delReq, ct).ConfigureAwait(false);
                    if (delResult == null || !delResult.Deleted)
                        success = false;
                }
                catch
                {
                    success = false;
                }
            }

            // Remove SQL record
            try
            {
                _db.MessageAttachments.Remove(e);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                success = false;
            }

            return success;
        }

        public async Task<string> GetPresignedUrlAsync(Guid attachmentId, TimeSpan expiry, CancellationToken ct = default)
        {
            var e = await _db.MessageAttachments!.AsNoTracking().FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, ct).ConfigureAwait(false);
            if (e == null || string.IsNullOrWhiteSpace(e.StoragePointer)) throw new InvalidOperationException("Attachment not found");

            // StorageContracts doesn't define a dedicated GetPresignedUrlAsync; try to return any AwsUrl/ObjectInfo.AwsUrl or Location
            try
            {
                // If the upload stored an AwsUrl in ObjectInfo, we can return it by re-querying object metadata (GetObject with ReturnStream=false)
                var key = ExtractKeyFromStoragePointer(e.StoragePointer, _bucketName);
                var getReq = new S3GetObjectRequest
                {
                    BucketName = _bucketName,
                    Key = key,
                    ReturnStream = false
                };

                var getResult = await _s3.GetObjectAsync(getReq, ct).ConfigureAwait(false);
                if (getResult != null && !string.IsNullOrWhiteSpace(getResult.Message))
                {
                    return getResult.Message;
                }
            }
            catch
            {
                // ignore
            }

            // Fallback: return storage pointer (caller can construct presigned URL if needed)
            return e.StoragePointer!;
        }

        #region Helpers

        private static string ExtractKeyFromStoragePointer(string storagePointer, string bucketName)
        {
            if (string.IsNullOrWhiteSpace(storagePointer)) return string.Empty;

            if (storagePointer.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
            {
                var without = storagePointer.Substring("s3://".Length);
                var idx = without.IndexOf('/');
                if (idx >= 0)
                    return without.Substring(idx + 1);
                return without;
            }

            var marker = "/" + bucketName + "/";
            var pos = storagePointer.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (pos >= 0)
            {
                return storagePointer.Substring(pos + marker.Length);
            }

            return storagePointer;
        }

        // Deterministic Guid <-> long conversions (used because EF entities use long? for user ids)
        private static long ConvertGuidToLong(Guid guid)
        {
            var bytes = guid.ToByteArray();
            return BitConverter.ToInt64(bytes, 0);
        }

        private static long? ConvertGuidToLongNullable(Guid? guid)
        {
            if (!guid.HasValue) return null;
            return ConvertGuidToLong(guid.Value);
        }

        private static Guid ConvertLongToGuid(long id)
        {
            var bytes = new byte[16];
            BitConverter.GetBytes(id).CopyTo(bytes, 0);
            return new Guid(bytes);
        }

        private static Guid? ConvertLongToGuidNullable(long? id)
        {
            if (!id.HasValue) return null;
            return ConvertLongToGuid(id.Value);
        }

        #endregion
    }
}





//// src/ProjectsPlus.Comms/Services/AttachmentService.cs
//using System;
//using System.IO;
//using System.Text.Json;
//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.EntityFrameworkCore;
//using t5f25sdprojectone_projectsplus.Data;
//using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.Interfaces;
//using t5f25sdprojectone_projectsplus.Models.Communication;
//using t5f25sdprojectone_projectsplus.Services.ComsService;

//namespace t5f25sdprojectone_projectsplus.Comms.Services
//{
//    /// <summary>
//    /// AttachmentService (corrected to use the StorageContracts interfaces)
//    /// - Uses IS3BucketService.PutObjectAsync / GetObjectAsync / DeleteObjectAsync
//    /// - Uses IDynamodbService.PutItemAsync / GetItemAsync / DeleteItemAsync with Ddb* request/response DTOs
//    /// - Persists pointer records in SQL Server via ProjectsPlusDbContext.MessageAttachments
//    /// </summary>
//    public class AttachmentService : IAttachmentService
//    {
//        private readonly ProjectsPlusDbContext _db;
//        private readonly IS3BucketService _s3;
//        private readonly IDynamodbService _ddb;
//        private readonly string _bucketName;
//        private readonly string _ddbTableName;

//        public AttachmentService(
//            ProjectsPlusDbContext db,
//            IS3BucketService s3BucketService,
//            IDynamodbService dynamoDbService)
//        {
//            _db = db ?? throw new ArgumentNullException(nameof(db));
//            _s3 = s3BucketService ?? throw new ArgumentNullException(nameof(s3BucketService));
//            _ddb = dynamoDbService ?? throw new ArgumentNullException(nameof(dynamoDbService));

//            // Read configured names from the concrete services' options if available.
//            // StorageContracts define GetOptions() on both interfaces.
//            try
//            {
//                var s3Opts = _s3.Options ?? (_s3 as dynamic)?.Options;
//                _bucketName = s3Opts?.BucketName ?? throw new InvalidOperationException("S3 bucket not configured");
//            }
//            catch
//            {
//                _bucketName = "projectsplus-attachments";
//            }

//            try
//            {
//                var ddbOpts = _ddb.Options ?? (_ddb as dynamic)?.Options;
//                _ddbTableName = ddbOpts?.TableName ?? "ProjectsPlusAttachments";
//            }
//            catch
//            {
//                _ddbTableName = "ProjectsPlusAttachments";
//            }
//        }

//        public async Task<AttachmentDescriptor> UploadAsync(Stream content, string filename, string contentType, Guid uploaderUserId, Guid? messageId = null, CancellationToken ct = default)
//        {
//            if (content == null) throw new ArgumentNullException(nameof(content));
//            if (string.IsNullOrWhiteSpace(filename)) throw new ArgumentNullException(nameof(filename));

//            var attachmentId = Guid.NewGuid();
//            var key = $"{attachmentId:N}/{filename}";

//            // 1) Upload to S3 via S3PutObjectRequest
//            string storagePointer;
//            S3PutObjectResult putResult;
//            try
//            {
//                var putReq = new S3PutObjectRequest
//                {
//                    BucketName = _bucketName,
//                    Key = key,
//                    Content = content,
//                    ContentType = contentType,
//                    ContentLength = content.CanSeek ? content.Length : null
//                };

//                putResult = await _s3.PutObjectAsync(putReq, ct).ConfigureAwait(false);

//                // Prefer returned Location/ObjectInfo.Location; fallback to s3://bucket/key
//                storagePointer = !string.IsNullOrWhiteSpace(putResult?.Location)
//                    ? putResult.Location
//                    : (putResult?.ObjectInfo?.Location ?? $"s3://{_bucketName}/{key}");
//            }
//            catch (Exception ex)
//            {
//                throw new InvalidOperationException("Failed to upload attachment to S3", ex);
//            }

//            // 2) Write metadata to DynamoDB (best-effort) using DdbPutRequest
//            DdbPutResult ddbPutResult = null!;
//            try
//            {
//                var item = new Dictionary<string, object?>
//                {
//                    ["AttachmentId"] = attachmentId.ToString("D"),
//                    ["Filename"] = filename,
//                    ["ContentType"] = contentType,
//                    ["StoragePointer"] = storagePointer,
//                    ["UploaderUserId"] = uploaderUserId.ToString("D"),
//                    ["MessageId"] = messageId?.ToString("D"),
//                    ["CreatedAt"] = DateTime.UtcNow.ToString("o")
//                };

//                var ddbReq = new DdbPutRequest
//                {
//                    TableName = _ddbTableName,
//                    Item = item,
//                    OverwriteIfExists = true
//                };

//                ddbPutResult = await _ddb.PutItemAsync(ddbReq, ct).ConfigureAwait(false);
//            }
//            catch
//            {
//                // ignore ddb failures (best-effort)
//                ddbPutResult = new DdbPutResult { Success = false };
//            }

//            // 3) Persist pointer in SQL Server
//            var entity = new MessageAttachmentEntity
//            {
//                AttachmentId = attachmentId,
//                MessageId = messageId ?? Guid.Empty,
//                Filename = filename,
//                ContentType = contentType,
//                StoragePointer = storagePointer,
//                // store a simple marker for DDB success; DDB doesn't return an id in our contract
//                DdbId = ddbPutResult != null && ddbPutResult.Success ? attachmentId.ToString("D") : null,
//                SizeBytes = content.CanSeek ? content.Length : (long?)null,
//                UploaderUserId = uploaderUserId,
//                CreatedAt = DateTime.UtcNow
//            };

//            await _db.MessageAttachments!.AddAsync(entity, ct).ConfigureAwait(false);
//            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

//            return new AttachmentDescriptor
//            {
//                AttachmentId = attachmentId,
//                MessageId = messageId,
//                Filename = filename,
//                ContentType = contentType,
//                SizeBytes = entity.SizeBytes,
//                StoragePointer = storagePointer,
//                DdbId = entity.DdbId,
//                UploaderUserId = entity.UploaderUserId ?? (Guid?)null,
//                CreatedAt = entity.CreatedAt
//            };
//        }

//        public async Task<Stream?> DownloadAsync(Guid attachmentId, CancellationToken ct = default)
//        {
//            var e = await _db.MessageAttachments!.AsNoTracking().FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, ct).ConfigureAwait(false);
//            if (e == null || string.IsNullOrWhiteSpace(e.StoragePointer)) return null;

//            try
//            {
//                // Build a GetObject request. StorageContracts expects Key + BucketName.
//                // If storagePointer is a full s3://... path, extract key portion.
//                var key = ExtractKeyFromStoragePointer(e.StoragePointer, _bucketName);

//                var getReq = new S3GetObjectRequest
//                {
//                    BucketName = _bucketName,
//                    Key = key,
//                    ReturnStream = true
//                };

//                var getResult = await _s3.GetObjectAsync(getReq, ct).ConfigureAwait(false);
//                if (getResult == null || !getResult.Found) return null;

//                return getResult.ContentStream;
//            }
//            catch
//            {
//                return null;
//            }
//        }

//        public async Task<AttachmentDescriptor?> GetMetadataAsync(Guid attachmentId, CancellationToken ct = default)
//        {
//            var e = await _db.MessageAttachments!.AsNoTracking().FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, ct).ConfigureAwait(false);
//            if (e == null) return null;

//            // Optionally fetch DynamoDB metadata (best-effort)
//            if (!string.IsNullOrWhiteSpace(e.DdbId))
//            {
//                try
//                {
//                    var key = new Dictionary<string, object?> { ["pk"] = e.DdbId };
//                    var getReq = new DdbGetRequest
//                    {
//                        TableName = _ddbTableName,
//                        Key = key,
//                        ConsistentRead = true
//                    };

//                    var getResult = await _ddb.GetItemAsync(getReq, ct).ConfigureAwait(false);
//                    // getResult.Item can be used/merged if desired
//                }
//                catch
//                {
//                    // ignore
//                }
//            }

//            return new AttachmentDescriptor
//            {
//                AttachmentId = e.AttachmentId,
//                MessageId = e.MessageId == Guid.Empty ? (Guid?)null : e.MessageId,
//                Filename = e.Filename ?? string.Empty,
//                ContentType = e.ContentType,
//                SizeBytes = e.SizeBytes,
//                StoragePointer = e.StoragePointer ?? string.Empty,
//                DdbId = e.DdbId,
//                UploaderUserId = e.UploaderUserId ?? (Guid?)null,
//                CreatedAt = e.CreatedAt
//            };
//        }

//        public async Task<bool> DeleteAsync(Guid attachmentId, CancellationToken ct = default)
//        {
//            var e = await _db.MessageAttachments!.FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, ct).ConfigureAwait(false);
//            if (e == null) return false;

//            var success = true;

//            // Delete from S3
//            try
//            {
//                var key = ExtractKeyFromStoragePointer(e.StoragePointer, _bucketName);
//                var delReq = new S3DeleteObjectRequest
//                {
//                    BucketName = _bucketName,
//                    Key = key,
//                    SkipIfNotFound = true
//                };

//                var delResult = await _s3.DeleteObjectAsync(delReq, ct).ConfigureAwait(false);
//                if (delResult == null || (!delResult.Deleted && !delResult.NotFound))
//                    success = false;
//            }
//            catch
//            {
//                success = false;
//            }

//            // Delete from DynamoDB (best-effort)
//            if (!string.IsNullOrWhiteSpace(e.DdbId))
//            {
//                try
//                {
//                    var key = new System.Collections.Generic.Dictionary<string, object?> { ["pk"] = e.DdbId };
//                    var delReq = new DdbDeleteRequest
//                    {
//                        TableName = _ddbTableName,
//                        Key = key,
//                        ReturnValues = false
//                    };

//                    var delResult = await _ddb.DeleteItemAsync(delReq, ct).ConfigureAwait(false);
//                    if (delResult == null || !delResult.Deleted)
//                        success = false;
//                }
//                catch
//                {
//                    success = false;
//                }
//            }

//            // Remove SQL record
//            try
//            {
//                _db.MessageAttachments.Remove(e);
//                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
//            }
//            catch
//            {
//                success = false;
//            }

//            return success;
//        }

//        public async Task<string> GetPresignedUrlAsync(Guid attachmentId, TimeSpan expiry, CancellationToken ct = default)
//        {
//            var e = await _db.MessageAttachments!.AsNoTracking().FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, ct).ConfigureAwait(false);
//            if (e == null || string.IsNullOrWhiteSpace(e.StoragePointer)) throw new InvalidOperationException("Attachment not found");

//            // StorageContracts does not define a dedicated GetPresignedUrlAsync method on IS3BucketService.
//            // If the S3 implementation returned an AwsUrl on upload, we can return that; otherwise return storagePointer as fallback.
//            // Try to call PutObjectAsync/ObjectInfo.AwsUrl by issuing a zero-op PutObject request is not appropriate here.
//            // So: attempt to call GetObjectAsync with ReturnStream=false and check Message/AwsUrl if implementation provides it.
//            try
//            {
//                var key = ExtractKeyFromStoragePointer(e.StoragePointer, _bucketName);
//                var getReq = new S3GetObjectRequest
//                {
//                    BucketName = _bucketName,
//                    Key = key,
//                    ReturnStream = false
//                };

//                var getResult = await _s3.GetObjectAsync(getReq, ct).ConfigureAwait(false);
//                // Some implementations may populate Message or Metadata with a presigned URL; check common fields
//                if (getResult != null)
//                {
//                    if (!string.IsNullOrWhiteSpace(getResult.Message))
//                        return getResult.Message;
//                }
//            }
//            catch
//            {
//                // ignore
//            }

//            // Fallback: return storagePointer (caller may interpret or construct a presigned URL)
//            return e.StoragePointer!;
//        }

//        #region Helpers

//        private static string ExtractKeyFromStoragePointer(string storagePointer, string bucketName)
//        {
//            // Accept forms: "s3://bucket/key", "s3://bucket/key?...", "https://.../bucket/key", or raw key
//            if (string.IsNullOrWhiteSpace(storagePointer)) return string.Empty;

//            // s3://bucket/key
//            if (storagePointer.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
//            {
//                var without = storagePointer.Substring("s3://".Length);
//                var idx = without.IndexOf('/');
//                if (idx >= 0)
//                    return without.Substring(idx + 1);
//                return without;
//            }

//            // https://s3.amazonaws.com/bucket/key or https://bucket.s3.amazonaws.com/key
//            // crude extraction: if contains bucketName, return substring after bucketName + '/'
//            var marker = "/" + bucketName + "/";
//            var pos = storagePointer.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
//            if (pos >= 0)
//            {
//                return storagePointer.Substring(pos + marker.Length);
//            }

//            // otherwise assume storagePointer is the key already
//            return storagePointer;
//        }

//        // Guid <-> long conversions (same as other repositories)
//        private static long ConvertGuidToLong(Guid guid)
//        {
//            var bytes = guid.ToByteArray();
//            return BitConverter.ToInt64(bytes, 0);
//        }

//        private static long? ConvertGuidToLongNullable(Guid? guid)
//        {
//            if (!guid.HasValue) return null;
//            return ConvertGuidToLong(guid.Value);
//        }

//        private static Guid ConvertLongToGuid(long id)
//        {
//            var bytes = new byte[16];
//            BitConverter.GetBytes(id).CopyTo(bytes, 0);
//            return new Guid(bytes);
//        }

//        private static Guid? ConvertLongToGuidNullable(long? id)
//        {
//            if (!id.HasValue) return null;
//            return ConvertLongToGuid(id.Value);
//        }

//        #endregion
//    }
//}
