//InfraService.cs
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Amazon.SecretsManager.Model;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus;

namespace t5f25sdprojectone_projectsplus.ScratchPlus
{
    public sealed class InfraService_bkp : IDisposable
    {
        private readonly IAmazonS3 _s3;
        private readonly IAmazonDynamoDB _ddb;
        private readonly Infralogger _logger;
        private readonly string _region = "us-east-2";
        private readonly RegionEndpoint _regionEndpoint;
        private readonly AWSCredentials _basic;
        private bool _disposed;

        public InfraService_bkp(IAmazonS3? s3 = null, IAmazonDynamoDB? ddb = null, Infralogger? logger = null)
        {
            _regionEndpoint = RegionEndpoint.USEast2;
            _basic = CredsReader.ReadFromCsv();
            _s3 = s3 ?? new AmazonS3Client(_basic, _regionEndpoint);
            _ddb = ddb ?? new AmazonDynamoDBClient(_basic, _regionEndpoint);

            var projectRoot = Directory.GetParent(Directory.GetCurrentDirectory())?.Parent?.Parent?.Parent?.FullName;
            var infralogsPath = projectRoot != null ? Path.Combine(projectRoot, "IaC_ProjectsPlus", "Infralogs.txt") : null!;
            _logger = logger ?? new Infralogger(infralogsPath);

            // defaults
            EnableDynamo = true;
            EnableS3 = true;
        }

        // Feature flags (can be toggled by Program.cs before wiring dependent services)
        public bool EnableDynamo { get; set; }
        public bool EnableS3 { get; set; }

        // Expose clients and environment info for DI consumers
        public IAmazonS3 S3Client => _s3;
        public IAmazonDynamoDB DdbClient => _ddb;
        public AWSCredentials Credentials => _basic;
        public string RegionName => _regionEndpoint.SystemName;
        public RegionEndpoint RegionEndpoint => _regionEndpoint;

        // Naming helpers
        public static string NormalizeBucketName(string baseName = "comp231003-91769-2025f-projectplus", string? envSuffix = null)
        {
            envSuffix ??= "";
            var name = (baseName + (string.IsNullOrEmpty(envSuffix) ? "" : $"-{envSuffix}")).ToLowerInvariant();
            var cleaned = new string(name.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-').ToArray());
            while (cleaned.Contains("--")) cleaned = cleaned.Replace("--", "-");
            if (cleaned.Length < 3) cleaned = cleaned.PadRight(3, 'x');
            if (cleaned.Length > 63) cleaned = cleaned.Substring(0, 63);
            cleaned = cleaned.Trim('-');
            if (cleaned.Length < 3) cleaned = "podcast-bucket";
            return cleaned;
        }

        public static string NormalizeTableName(string baseName = "comp306-lab3-PodcastComments")
        {
            var cleaned = new string(baseName.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Podcast-Comments";
            return cleaned;
        }

        // Ensure S3 bucket exists; returns normalized bucket name
        public async Task<string> EnsureS3BucketAsync(string bucketBaseName, string? envSuffix = null)
        {
            if (!EnableS3) throw new InvalidOperationException("S3 is disabled in InfraService");
            if (string.IsNullOrWhiteSpace(bucketBaseName)) throw new ArgumentNullException(nameof(bucketBaseName));

            var bucketName = NormalizeBucketName(bucketBaseName, envSuffix);
            var exists = await AmazonS3Util.DoesS3BucketExistV2Async(_s3, bucketName).ConfigureAwait(false);
            if (exists)
            {
                var rec = new ResourceRecord { ResourceType = "S3Bucket", Name = bucketName, Id = bucketName, Region = _region, CreatedAt = DateTime.UtcNow };
                if (!LoggerHasRecord(rec)) await _logger.AppendAsync(rec).ConfigureAwait(false);
                return bucketName;
            }

            var putReq = new PutBucketRequest { BucketName = bucketName, BucketRegionName = _region };
            await _s3.PutBucketAsync(putReq).ConfigureAwait(false);

            var block = new PutPublicAccessBlockRequest
            {
                BucketName = bucketName,
                PublicAccessBlockConfiguration = new PublicAccessBlockConfiguration
                {
                    BlockPublicAcls = true,
                    BlockPublicPolicy = true,
                    IgnorePublicAcls = true,
                    RestrictPublicBuckets = true
                }
            };
            await _s3.PutPublicAccessBlockAsync(block).ConfigureAwait(false);

            var arn = $"arn:aws:s3:::{bucketName}";
            var record = new ResourceRecord { ResourceType = "S3Bucket", Name = bucketName, Id = arn, Region = _region, CreatedAt = DateTime.UtcNow };
            await _logger.AppendAsync(record).ConfigureAwait(false);
            return bucketName;
        }

        // Ensure DynamoDB table exists; returns table name
        public async Task<string> EnsureDynamoTableAsync(string tableBaseName, int readCapacity = 5, int writeCapacity = 5)
        {
            if (!EnableDynamo) throw new InvalidOperationException("Dynamo is disabled in InfraService");
            if (string.IsNullOrWhiteSpace(tableBaseName)) throw new ArgumentNullException(nameof(tableBaseName));

            var tableName = NormalizeTableName(tableBaseName);

            try
            {
                var describe = await _ddb.DescribeTableAsync(new DescribeTableRequest { TableName = tableName }).ConfigureAwait(false);
                // avoid relying on the TableStatus enum type presence; stringify and compare
                if (describe?.Table != null && string.Equals(describe.Table.TableStatus?.ToString(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
                {
                    var rec = new ResourceRecord { ResourceType = "DynamoDBTable", Name = tableName, Id = tableName, Region = _region, CreatedAt = DateTime.UtcNow };
                    if (!LoggerHasRecord(rec)) await _logger.AppendAsync(rec).ConfigureAwait(false);
                    return tableName;
                }
            }
            catch (Amazon.DynamoDBv2.Model.ResourceNotFoundException) { }

            var request = new CreateTableRequest
            {
                TableName = tableName,
                AttributeDefinitions = new List<AttributeDefinition>
                {
                    new AttributeDefinition("CommentId", ScalarAttributeType.S)
                },
                KeySchema = new List<KeySchemaElement>
                {
                    new KeySchemaElement("CommentId", KeyType.HASH)
                },
                ProvisionedThroughput = new ProvisionedThroughput
                {
                    ReadCapacityUnits = readCapacity,
                    WriteCapacityUnits = writeCapacity
                }
            };

            var resp = await _ddb.CreateTableAsync(request).ConfigureAwait(false);

            // Wait until ACTIVE (string comparison to avoid TableStatus type issues)
            const int maxAttempts = 30;
            const int delayMs = 2000;
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    var d = await _ddb.DescribeTableAsync(new DescribeTableRequest { TableName = tableName }).ConfigureAwait(false);
                    if (d?.Table != null && string.Equals(d.Table.TableStatus?.ToString(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
                    {
                        var arn = resp.TableDescription?.TableArn ?? tableName;
                        var record = new ResourceRecord { ResourceType = "DynamoDBTable", Name = tableName, Id = arn, Region = _region, CreatedAt = DateTime.UtcNow };
                        await _logger.AppendAsync(record).ConfigureAwait(false);
                        return tableName;
                    }
                }
                catch (Amazon.DynamoDBv2.Model.ResourceNotFoundException) { }
                await Task.Delay(delayMs).ConfigureAwait(false);
            }

            throw new TimeoutException($"Timed out waiting for DynamoDB table {tableName} to become ACTIVE");
        }

        /// <summary>
        /// Ensures encrypted RDS secrets exist in AWS SSM Parameter Store.
        /// If missing, creates a secure password and connection string, stores them as SecureString parameters,
        /// and logs each created parameter for audit and deletion.
        /// </summary>
        /// <param name="dbName">Logical database name (used in connection string)</param>
        /// <param name="dbUser">Database username (default: admin)</param>
        /// <param name="envSuffix">Environment suffix (e.g., "prod", "dev")</param>
        /// <param name="dbHost">Optional DB host override (default: inferred from region)</param>
        /// <param name="port">Database port (default: 5432)</param>
        /// <returns>Tuple of parameter names: (passwordParamName, connectionParamName)</returns>
        public async Task<(string PasswordParamName, string ConnectionParamName)> EnsureRDSSecretsSSMAsync(
            string dbName,
            string dbUser = "admin",
            string envSuffix = "prod",
            string? dbHost = null,
            int port = 5432)
        {
            if (string.IsNullOrWhiteSpace(dbName))
                throw new ArgumentNullException(nameof(dbName));

            var ssm = new AmazonSimpleSystemsManagementClient(_basic, _regionEndpoint);

            var prefix = $"/podcastapp/{envSuffix}/db";
            var passwordParam = $"{prefix}/password";
            var connParam = $"{prefix}/connectionstring";

            async Task<bool> ParameterExistsAsync(string name)
            {
                try
                {
                    var response = await ssm.GetParameterAsync(new GetParameterRequest
                    {
                        Name = name,
                        WithDecryption = false
                    });
                    return response?.Parameter != null;
                }
                catch (ParameterNotFoundException)
                {
                    return false;
                }
            }

            async Task PutSecretAsync(string name, string value)
            {
                await ssm.PutParameterAsync(new PutParameterRequest
                {
                    Name = name,
                    Value = value,
                    Type = ParameterType.SecureString,
                    Overwrite = false
                });

                var record = new ResourceRecord
                {
                    ResourceType = "SSMParameter",
                    Name = name,
                    Id = name,
                    Region = _region,
                    CreatedAt = DateTime.UtcNow
                };

                await _logger.AppendAsync(record).ConfigureAwait(false);
            }

            // Check and create password if missing
            if (!await ParameterExistsAsync(passwordParam))
            {
                var password = $"DbPass_{Guid.NewGuid():N}".Substring(0, 16);
                await PutSecretAsync(passwordParam, password);
            }

            // Check and create connection string if missing
            if (!await ParameterExistsAsync(connParam))
            {
                var host = dbHost ?? $"podcast-db.{_region}.rds.amazonaws.com";
                var password = await ssm.GetParameterAsync(new GetParameterRequest
                {
                    Name = passwordParam,
                    WithDecryption = true
                });

                var connectionString = $"Host={host};Port={port};Database={dbName};Username={dbUser};Password={password.Parameter.Value};";
                await PutSecretAsync(connParam, connectionString);
            }

            return (passwordParam, connParam);
        }

        /// <summary>
        /// Ensures RDS SQL Server credentials are stored in AWS Secrets Manager.
        /// If missing, creates a new secret with generated password and logs it for auditability and deletion.
        /// </summary>
        /// <param name="dbName">Logical database name</param>
        /// <param name="dbUser">Database username (default: admin)</param>
        /// <param name="envSuffix">Environment suffix (e.g., "prod", "dev")</param>
        /// <param name="dbHost">Optional DB host override</param>
        /// <param name="port">Database port (default: 1433)</param>
        /// <returns>Secret name that holds the credentials</returns>
        public async Task<string> EnsureRDSSecretsSMSAsync(
            string dbName,
            string dbUser = "admin",
            string envSuffix = "prod",
            string? dbHost = null,
            int port = 1433)
        {
            if (string.IsNullOrWhiteSpace(dbName))
                throw new ArgumentNullException(nameof(dbName));

            var sm = new Amazon.SecretsManager.AmazonSecretsManagerClient(_basic, _regionEndpoint);
            var secretName = $"podcastapp/{envSuffix}/rds";

            // Check if secret exists
            try
            {
                var describe = await sm.DescribeSecretAsync(new DescribeSecretRequest
                {
                    SecretId = secretName
                });

                // Already exists — log if not already recorded
                var record = new ResourceRecord
                {
                    ResourceType = "SecretsManagerSecret",
                    Name = secretName,
                    Id = describe.ARN,
                    Region = _region,
                    CreatedAt = DateTime.UtcNow
                };

                if (!await _logger.ExistsAsync(record))
                    await _logger.AppendAsync(record).ConfigureAwait(false);

                return secretName;
            }
            catch (Amazon.SecretsManager.Model.ResourceNotFoundException)
            {
                // Proceed to create
            }

            // Generate password and construct secret payload
            var password = $"DbPass_{Guid.NewGuid():N}".Substring(0, 16);
            var host = dbHost ?? $"podcast-db.{_region}.rds.amazonaws.com";

            var secretJson = $@"{{
                ""username"": ""{dbUser}"",
                ""password"": ""{password}"",
                ""engine"": ""sqlserver-ex"",
                ""host"": ""{host}"",
                ""port"": {port},
                ""dbname"": ""{dbName}""
            }}";

            var create = await sm.CreateSecretAsync(new CreateSecretRequest
            {
                Name = secretName,
                SecretString = secretJson
            });

            var newRecord = new ResourceRecord
            {
                ResourceType = "SecretsManagerSecret",
                Name = secretName,
                Id = create.ARN,
                Region = _region,
                CreatedAt = DateTime.UtcNow
            };

            await _logger.AppendAsync(newRecord).ConfigureAwait(false);
            return secretName;
        }

        // Delete all resources recorded in InfraLogger (DynamoDB then S3)
        public async Task DeleteAllAsync()
        {
            var entries = _logger.ReadAll();
            if (entries == null || !entries.Any()) return;

            var tables = entries.Where(e => string.Equals(e.ResourceType, "DynamoDBTable", StringComparison.OrdinalIgnoreCase)).ToList();
            var buckets = entries.Where(e => string.Equals(e.ResourceType, "S3Bucket", StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var t in tables)
            {
                try
                {
                    await _ddb.DeleteTableAsync(new DeleteTableRequest { TableName = t.Name }).ConfigureAwait(false);
                }
                catch (Amazon.DynamoDBv2.Model.ResourceNotFoundException) { }
                catch (Exception) { /* log and continue */ }
            }

            foreach (var b in buckets)
            {
                try
                {
                    var listResp = await _s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = b.Name }).ConfigureAwait(false);
                    if (listResp?.S3Objects != null && listResp.S3Objects.Count > 0)
                    {
                        var deleteReq = new DeleteObjectsRequest { BucketName = b.Name };
                        deleteReq.Objects = listResp.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList();
                        await _s3.DeleteObjectsAsync(deleteReq).ConfigureAwait(false);
                    }

                    await _s3.DeleteBucketAsync(new DeleteBucketRequest { BucketName = b.Name }).ConfigureAwait(false);
                }
                catch (AmazonS3Exception) { /* ignore and continue */ }
                catch (Exception) { /* ignore and continue */ }
            }

            _logger.Clear();
        }

        private bool LoggerHasRecord(ResourceRecord rec)
        {
            var all = _logger.ReadAll();
            return all.Any(r => string.Equals(r.ResourceType, rec.ResourceType, StringComparison.OrdinalIgnoreCase)
                              && string.Equals(r.Name, rec.Name, StringComparison.OrdinalIgnoreCase));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _s3?.Dispose();
            _ddb?.Dispose();
            _disposed = true;
        }
    }
}
