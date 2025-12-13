// ServiceCollectionDynamoExtensions.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.EC2;
using Amazon.RDS;
using Amazon.Runtime;
using Amazon.SecretsManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules;
using t5f25sdprojectone_projectsplus.ScratchPlus;
using static t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules.EnsureDDB;
using static t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules.EnsureRDS;
using static t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules.EnsureS3B;
using Infralogger = t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules.Infralogger;

namespace t5f25sdprojectone_projectsplus.RegExtension
{
    public static class ServiceCollectionRDSExtensions
    {
        /// <summary>
        /// Ensures the DynamoDB table exists, then registers the initialized AmazonDynamoDBClient and DynamodbService.
        /// Call and await this BEFORE calling builder.Build()/host.RunAsync().
        /// </summary>
        public static async Task AddAndInitializeRDSAsync(
            this IServiceCollection services,
            IConfiguration configuration,
            RegionEndpoint? region = null,
            CancellationToken cancellationToken = default)
        {            
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));            

            region ??= RegionEndpoint.USEast2;

            // Create client (uses same CredsReader.ReadFromCsv() pattern as RDS extension)
            var creds = CredsReader.ReadFromCsv();
            var rdsClient = new AmazonRDSClient(creds, region);
            var ec2Client = new AmazonEC2Client(creds, region);
            var asmClient = new AmazonSecretsManagerClient(creds, region);

            using var tempProvider = services.BuildServiceProvider();
            var systemlogger = tempProvider.GetService<ILoggerFactory>()?.CreateLogger("RDSInit");

            EnsureRdsResult rdsInfra;
            try
            {
                var ensure = new EnsureRDS(rdsClient, ec2Client, asmClient, new Infralogger(), region.SystemName);
                rdsInfra = await ensure.EnsureCreateAsync(new EnsureRdsRequest(), cancellationToken).ConfigureAwait(false);
                //_ = await ensure.EnsureDestroyAsync(dbInstanceNameOrArn: rdsInfra.DBInstanceIdentifier, ct: cancellationToken); // destroy
            }
            catch (OperationCanceledException)
            {
                // propagate cancellation
                try { rdsClient.Dispose(); ec2Client.Dispose(); asmClient.Dispose(); } catch { }                
                throw;
            }
            catch (Exception ex)
            {
                // dispose client if ensure fails and rethrow so startup fails visibly
                try { rdsClient.Dispose(); ec2Client.Dispose(); asmClient.Dispose(); } catch { }
                systemlogger?.LogError(ex, "S3 EnsureBucketAsync failed during startup.");
                throw;
            }            
           

            // Register singletons. Register the client instance so DI will dispose it on shutdown.
            services.AddSingleton<IAmazonRDS>(rdsClient);
            services.AddSingleton<IAmazonEC2>(ec2Client);
            services.AddSingleton<IAmazonSecretsManager>(asmClient);
            services.AddSingleton(rdsInfra);            
        }
    }
}
