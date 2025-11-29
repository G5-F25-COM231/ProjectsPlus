// ServiceCollectionS3Extensions.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules;
using t5f25sdprojectone_projectsplus.Services;
using static t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules.EnsureS3;
using Infralogger = t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules.Infralogger;

namespace t5f25sdprojectone_projectsplus.RegExtension
{
    public static class ServiceCollectionS3Extensions
    {
        /// <summary>
        /// Ensures the S3 bucket exists, then registers the initialized AmazonS3Client and S3BucketService.
        /// Call and await this BEFORE calling builder.Build()/host.RunAsync().
        /// </summary>
        public static async Task AddAndInitializeS3Async(
            this IServiceCollection services,
            IConfiguration configuration,            
            RegionEndpoint? region = null,
            CancellationToken cancellationToken = default)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            region ??= RegionEndpoint.USEast2;        
                       
            // Create client
            var s3Client = new AmazonS3Client(CredsReader.ReadFromCsv(), region);

            using var tempProvider = services.BuildServiceProvider();
            var systemlogger = tempProvider.GetService<ILoggerFactory>()?.CreateLogger("S3Init");

            // Run ensure BEFORE registering the dependent service
            EnsureS3Result s3Infra;
            try
            {
                var ensure = new EnsureS3(s3Client, new Infralogger(), region.SystemName);
                s3Infra = await ensure.EnsureBucketAsync(new EnsureS3Request(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // propagate cancellation
                try { s3Client.Dispose(); } catch { }
                throw;
            }
            catch (Exception ex)
            {
                // dispose client if ensure fails and rethrow so startup fails visibly
                try { s3Client.Dispose(); } catch { }
                systemlogger?.LogError(ex, "S3 EnsureBucketAsync failed during startup.");
                throw;
            }

            // Create the fully-initialized service instance (assumes S3BucketService has ctor (IAmazonS3, S3Infra))
            var s3Service = new S3BucketService(s3Client, s3Infra);

            // Register singletons. Register the client instance so DI will dispose it on shutdown.
            services.AddSingleton<IAmazonS3>(s3Client);
            services.AddSingleton(s3Service);

            // Optionally register an interface if S3BucketService implements one:
            services.AddSingleton<IS3BucketService>(s3Service);
        }
    }


   
   
}
