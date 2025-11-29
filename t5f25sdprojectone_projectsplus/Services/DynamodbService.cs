// src/IaC_ProjectsPlus/DynamodbService.cs
//
// Implementation of IDynamodbService (constructor-configured).
// Namespace: t5f25sdprojectone_projectsplus.IaC_ProjectsPlus

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus
{
    public sealed class DynamodbServiceOptions
    {
        public string TableName { get; init; } = string.Empty;
        public string PartitionKeyName { get; init; } = "pk";
        public string? SortKeyName { get; init; } = null;
        public bool OnDemandBilling { get; init; } = true;
        public long ReadCapacityUnits { get; init; } = 5;
        public long WriteCapacityUnits { get; init; } = 5;
    }

    public class DynamodbService : IDynamodbService, IDisposable
    {
        private readonly IAmazonDynamoDB _client;
        private readonly DynamodbServiceOptions _options;
        private bool _disposed;

        public DynamodbService(IAmazonDynamoDB client, DynamodbServiceOptions options)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(_options.TableName))
                throw new ArgumentException("TableName must be provided in options.", nameof(options));
        }

        public DynamodbServiceOptions Options => _options; 

        public async Task<DdbPutResult> PutItemAsync(DdbPutRequest req, CancellationToken ct = default)
        {
            var tableName = string.IsNullOrWhiteSpace(req?.TableName) ? _options.TableName : req.TableName;
            try
            {
                var request = new PutItemRequest
                {
                    TableName = tableName,
                    Item = ToAttributeMap(req.Item)
                };

                if (!req.OverwriteIfExists)
                {
                    request.ConditionExpression = $"attribute_not_exists({_options.PartitionKeyName})";
                }

                await _client.PutItemAsync(request, ct).ConfigureAwait(false);
                return new DdbPutResult { Success = true };
            }
            catch (Exception ex)
            {
                return new DdbPutResult { Success = false, Message = ex.Message };
            }
        }

        public async Task<DdbGetResult> GetItemAsync(DdbGetRequest req, CancellationToken ct = default)
        {
            var tableName = string.IsNullOrWhiteSpace(req?.TableName) ? _options.TableName : req.TableName;
            try
            {
                var request = new GetItemRequest
                {
                    TableName = tableName,
                    Key = ToAttributeMap(req.Key),
                    ConsistentRead = req.ConsistentRead
                };

                var resp = await _client.GetItemAsync(request, ct).ConfigureAwait(false);
                if (resp.Item == null || resp.Item.Count == 0)
                    return new DdbGetResult { Found = false };

                return new DdbGetResult
                {
                    Found = true,
                    Item = FromAttributeMap(resp.Item)
                };
            }
            catch (Exception ex)
            {
                return new DdbGetResult { Found = false, Message = ex.Message };
            }
        }

        public async Task<DdbDeleteResult> DeleteItemAsync(DdbDeleteRequest req, CancellationToken ct = default)
        {
            var tableName = string.IsNullOrWhiteSpace(req?.TableName) ? _options.TableName : req.TableName;
            try
            {
                var request = new DeleteItemRequest
                {
                    TableName = tableName,
                    Key = ToAttributeMap(req.Key),
                    ReturnValues = req.ReturnValues ? ReturnValue.ALL_OLD : ReturnValue.NONE
                };

                var resp = await _client.DeleteItemAsync(request, ct).ConfigureAwait(false);

                return new DdbDeleteResult
                {
                    Deleted = true,
                    OldItem = resp.Attributes != null && resp.Attributes.Count > 0 ? FromAttributeMap(resp.Attributes) : null
                };
            }
            catch (Exception ex)
            {
                return new DdbDeleteResult { Deleted = false, Message = ex.Message };
            }
        }

        public async Task<DdbQueryResult> QueryAsync(DdbQueryRequest req, CancellationToken ct = default)
        {
            var tableName = string.IsNullOrWhiteSpace(req?.TableName) ? _options.TableName : req.TableName;
            try
            {
                var request = new QueryRequest
                {
                    TableName = tableName,
                    KeyConditionExpression = req.KeyConditionExpression,
                    ExpressionAttributeValues = ToAttributeMap(req.ExpressionAttributeValues),
                    FilterExpression = req.FilterExpression,
                    Limit = req.Limit
                };

                var resp = await _client.QueryAsync(request, ct).ConfigureAwait(false);

                return new DdbQueryResult
                {
                    Items = resp.Items?.Select(FromAttributeMap).ToArray() ?? Array.Empty<IDictionary<string, object?>>(),
                    LastEvaluatedKeyJson = resp.LastEvaluatedKey != null && resp.LastEvaluatedKey.Count > 0 ? SerializeAttributeMap(resp.LastEvaluatedKey) : null,
                    ScannedCount = (long)resp.ScannedCount
                };
            }
            catch (Exception)
            {
                return new DdbQueryResult { Items = Array.Empty<IDictionary<string, object?>>(), LastEvaluatedKeyJson = null, ScannedCount = 0 };
            }
        }

        public async Task<DdbScanResult> ScanAsync(DdbScanRequest req, CancellationToken ct = default)
        {
            var tableName = string.IsNullOrWhiteSpace(req?.TableName) ? _options.TableName : req.TableName;
            try
            {
                var request = new ScanRequest
                {
                    TableName = tableName,
                    FilterExpression = req.FilterExpression,
                    ExpressionAttributeValues = ToAttributeMap(req.ExpressionAttributeValues),
                    Limit = req.Limit
                };

                var resp = await _client.ScanAsync(request, ct).ConfigureAwait(false);

                return new DdbScanResult
                {
                    Items = resp.Items?.Select(FromAttributeMap).ToArray() ?? Array.Empty<IDictionary<string, object?>>(),
                    LastEvaluatedKeyJson = resp.LastEvaluatedKey != null && resp.LastEvaluatedKey.Count > 0 ? SerializeAttributeMap(resp.LastEvaluatedKey) : null,
                    ScannedCount = (long)resp.ScannedCount
                };
            }
            catch (Exception)
            {
                return new DdbScanResult { Items = Array.Empty<IDictionary<string, object?>>(), LastEvaluatedKeyJson = null, ScannedCount = 0 };
            }
        }

        public async Task<DdbEnsureTableResult> EnsureTableAsync(DdbEnsureTableRequest req, CancellationToken ct = default)
        {
            var tableName = string.IsNullOrWhiteSpace(req?.TableName) ? _options.TableName : req.TableName;
            var partitionKey = string.IsNullOrWhiteSpace(req?.PartitionKeyName) ? _options.PartitionKeyName : req.PartitionKeyName;
            var sortKey = string.IsNullOrWhiteSpace(req?.SortKeyName) ? _options.SortKeyName : req.SortKeyName;
            var onDemand = req?.OnDemandBilling ?? _options.OnDemandBilling;
            var readUnits = (req?.ReadCapacityUnits ?? 0) != 0 ? req!.ReadCapacityUnits : _options.ReadCapacityUnits;
            var writeUnits = (req?.WriteCapacityUnits ?? 0) != 0 ? req!.WriteCapacityUnits : _options.WriteCapacityUnits;

            try
            {
                var describe = await _client.ListTablesAsync(ct).ConfigureAwait(false);
                if (describe.TableNames.Contains(tableName))
                {
                    return new DdbEnsureTableResult { Created = false, AlreadyExisted = true };
                }

                var attributeDefinitions = new List<AttributeDefinition>
                {
                    new AttributeDefinition { AttributeName = partitionKey, AttributeType = "S" }
                };

                var keySchema = new List<KeySchemaElement>
                {
                    new KeySchemaElement { AttributeName = partitionKey, KeyType = KeyType.HASH }
                };

                if (!string.IsNullOrEmpty(sortKey))
                {
                    attributeDefinitions.Add(new AttributeDefinition { AttributeName = sortKey, AttributeType = "S" });
                    keySchema.Add(new KeySchemaElement { AttributeName = sortKey, KeyType = KeyType.RANGE });
                }

                var createReq = new CreateTableRequest
                {
                    TableName = tableName,
                    AttributeDefinitions = attributeDefinitions,
                    KeySchema = keySchema
                };

                if (onDemand)
                {
                    createReq.BillingMode = BillingMode.PAY_PER_REQUEST;
                }
                else
                {
                    createReq.ProvisionedThroughput = new ProvisionedThroughput(readUnits, writeUnits);
                }

                var resp = await _client.CreateTableAsync(createReq, ct).ConfigureAwait(false);

                return new DdbEnsureTableResult { Created = true, AlreadyExisted = false, TableArn = resp.TableDescription?.TableArn };
            }
            catch (Exception ex)
            {
                return new DdbEnsureTableResult { Created = false, AlreadyExisted = false, Message = ex.Message };
            }
        }

        public async Task<DdbDestroyTableResult> DestroyTableAsync(DdbDestroyTableRequest req, CancellationToken ct = default)
        {
            var tableName = string.IsNullOrWhiteSpace(req?.TableName) ? _options.TableName : req.TableName;
            try
            {
                var tables = await _client.ListTablesAsync(ct).ConfigureAwait(false);
                if (!tables.TableNames.Contains(tableName))
                {
                    return new DdbDestroyTableResult { Destroyed = false, NotFound = true };
                }

                await _client.DeleteTableAsync(new DeleteTableRequest { TableName = tableName }, ct).ConfigureAwait(false);
                return new DdbDestroyTableResult { Destroyed = true, NotFound = false };
            }
            catch (Exception ex)
            {
                return new DdbDestroyTableResult { Destroyed = false, Message = ex.Message };
            }
        }

        public async Task<DdbBatchWriteResult> BatchWriteAsync(DdbBatchWriteRequest req, CancellationToken ct = default)
        {
            var tableName = string.IsNullOrWhiteSpace(req?.TableName) ? _options.TableName : req.TableName;
            try
            {
                var writeRequests = new List<WriteRequest>();

                foreach (var item in req.ItemsToPut)
                {
                    writeRequests.Add(new WriteRequest { PutRequest = new PutRequest { Item = ToAttributeMap(item) } });
                }

                foreach (var key in req.KeysToDelete)
                {
                    writeRequests.Add(new WriteRequest { DeleteRequest = new DeleteRequest { Key = ToAttributeMap(key) } });
                }

                var batch = new BatchWriteItemRequest
                {
                    RequestItems = new Dictionary<string, List<WriteRequest>> { { tableName, writeRequests } }
                };

                var resp = await _client.BatchWriteItemAsync(batch, ct).ConfigureAwait(false);

                return new DdbBatchWriteResult
                {
                    PutCount = req.ItemsToPut.Count,
                    DeleteCount = req.KeysToDelete.Count,
                    UnprocessedItems = resp.UnprocessedItems != null && resp.UnprocessedItems.TryGetValue(tableName, out var unprocessed)
                        ? unprocessed.Select(FromWriteRequest).ToArray()
                        : null
                };
            }
            catch (Exception ex)
            {
                return new DdbBatchWriteResult { PutCount = 0, DeleteCount = 0, Message = ex.Message };
            }
        }

        #region Helpers

        private static Dictionary<string, AttributeValue> ToAttributeMap(IDictionary<string, object?>? dict)
        {
            var map = new Dictionary<string, AttributeValue>();
            if (dict == null) return map;
            foreach (var kv in dict)
            {
                map[kv.Key] = ToAttributeValue(kv.Value);
            }
            return map;
        }

        private static AttributeValue ToAttributeValue(object? value)
        {
            if (value == null) return new AttributeValue { NULL = true };
            switch (value)
            {
                case string s: return new AttributeValue { S = s };
                case bool b: return new AttributeValue { BOOL = b };
                case int i: return new AttributeValue { N = i.ToString() };
                case long l: return new AttributeValue { N = l.ToString() };
                case float f: return new AttributeValue { N = Convert.ToString(f, System.Globalization.CultureInfo.InvariantCulture) };
                case double d: return new AttributeValue { N = Convert.ToString(d, System.Globalization.CultureInfo.InvariantCulture) };
                case decimal m: return new AttributeValue { N = Convert.ToString(m, System.Globalization.CultureInfo.InvariantCulture) };
                case IDictionary<string, object?> dict:
                    return new AttributeValue { M = ToAttributeMap(dict) };
                case IEnumerable<object?> list:
                    return new AttributeValue { L = list.Select(ToAttributeValue).ToList() };
                default:
                    return new AttributeValue { S = value.ToString() ?? string.Empty };
            }
        }

        private static IDictionary<string, object?> FromAttributeMap(IDictionary<string, AttributeValue> map)
        {
            var result = new Dictionary<string, object?>();
            foreach (var kv in map)
            {
                result[kv.Key] = FromAttributeValue(kv.Value);
            }
            return result;
        }

        private static object? FromAttributeValue(AttributeValue av)
        {
            if ((bool)av.NULL) return null;
            if (av.S != null) return av.S;
            if (av.N != null)
            {
                if (long.TryParse(av.N, out var l)) return l;
                if (double.TryParse(av.N, out var d)) return d;
                return av.N;
            }
            if (av.BOOL.HasValue) return av.BOOL.Value;
            if (av.M != null && av.M.Count > 0) return FromAttributeMap(av.M);
            if (av.L != null && av.L.Count > 0) return av.L.Select(FromAttributeValue).ToList();
            return null;
        }

        private static string SerializeAttributeMap(IDictionary<string, AttributeValue> map)
        {
            return System.Text.Json.JsonSerializer.Serialize(map.ToDictionary(k => k.Key, v => v.Value.S ?? v.Value.N ?? ((bool)v.Value.NULL ? "null" : "")));
        }

        private static IDictionary<string, object?>? FromWriteRequest(WriteRequest wr)
        {
            if (wr.PutRequest != null) return FromAttributeMap(wr.PutRequest.Item);
            if (wr.DeleteRequest != null) return wr.DeleteRequest.Key.ToDictionary(k => k.Key, v => (object?)v.Value.S ?? (object?)v.Value.N);
            return null;
        }

        #endregion

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
