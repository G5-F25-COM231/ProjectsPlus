using Amazon.DynamoDBv2;

namespace t5f25sdprojectone_projectsplus.Services
{
    public interface ITableNameProvider
    {
        /// <summary>
        /// Returns the DynamoDB table name. Guaranteed non-empty.
        /// </summary>
        string TableName { get; }
    }

    public class TableNameProvider : ITableNameProvider
    {
        public string TableName { get; }

        private readonly IServiceProvider _services;
        private static DynamodbService _ddbsvc;
        public TableNameProvider(IServiceProvider services, IConfiguration configuration, ILogger<TableNameProvider> logger)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _ddbsvc = _services.GetRequiredService<DynamodbService>();

            // read from configuration key DynamoDb:TableName
            var name = _ddbsvc.Options.TableName;

            if (string.IsNullOrWhiteSpace(name))
            {
                logger.LogError("DynamoDb:TableName is not configured or is empty.");
                throw new InvalidOperationException("DynamoDB table name is not configured. Set DynamoDb:TableName in configuration.");
            }

            TableName = name;
            logger.LogInformation("Using DynamoDB table name {TableName}", TableName);
        }

        public static DdbSpecsDto GetDdbSpecs => new()
        {
            DdbClient = _ddbsvc.DdbClient,
            TableName = _ddbsvc.Options.TableName
        };


    }


    public class DdbSpecsDto
    {
        public IAmazonDynamoDB? DdbClient { get; set; }
        public string? TableName { get; set; }
    }


}
