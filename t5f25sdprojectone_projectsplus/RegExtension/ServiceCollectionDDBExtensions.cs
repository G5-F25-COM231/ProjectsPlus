// ServiceCollectionDynamoExtensions.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.Interfaces;
using t5f25sdprojectone_projectsplus.Services;
using static t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules.EnsureDDB;
using static t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules.EnsureS3B;
using Infralogger = t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules.Infralogger;

namespace t5f25sdprojectone_projectsplus.RegExtension
{
    public static class ServiceCollectionDDExtensions
    {
        /// <summary>
        /// Ensures the DynamoDB table exists, then registers the initialized AmazonDynamoDBClient and DynamodbService.
        /// Call and await this BEFORE calling builder.Build()/host.RunAsync().
        /// </summary>
        public static async Task AddAndInitializeDDbAsync(
            this IServiceCollection services,
            IConfiguration configuration,
            RegionEndpoint? region = null,
            CancellationToken cancellationToken = default)
        {            
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));            

            region ??= RegionEndpoint.USEast2;

            // Create client (uses same CredsReader.ReadFromCsv() pattern as S3 extension)
            var ddbClient = new AmazonDynamoDBClient(CredsReader.ReadFromCsv(), region);

            using var tempProvider = services.BuildServiceProvider();
            var systemlogger = tempProvider.GetService<ILoggerFactory>()?.CreateLogger("DynamoInit");
            
            EnsureDdbResult ddbInfra;
            try
            {
                var ensure = new EnsureDDB(ddbClient, new Infralogger(), region.SystemName);
                ddbInfra = await ensure.EnsureCreateAsync(new EnsureDdbRequest(), cancellationToken).ConfigureAwait(false);
                //_ = await ensure.EnsureDestroyAsync(ddbInfra.TableName, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // propagate cancellation
                try { ddbClient.Dispose(); } catch { }
                throw;
            }
            catch (Exception ex)
            {
                // dispose client if ensure fails and rethrow so startup fails visibly
                try { ddbClient.Dispose(); } catch { }
                systemlogger?.LogError(ex, "S3 EnsureBucketAsync failed during startup.");
                throw;
            }

            
            DynamodbServiceOptions options = new() { TableName = ddbInfra.TableName };
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.TableName)) throw new ArgumentException("TableName must be provided in options.", nameof(options));

            // Create the fully-initialized service instance
            var dynamoService = new DynamodbService(ddbClient, options);

            // Register singletons. Register the client instance so DI will dispose it on shutdown.
            services.AddSingleton<IAmazonDynamoDB>(ddbClient);
            services.AddSingleton(dynamoService);
            services.AddSingleton<IDynamodbService>(dynamoService);
        }
    }
}
