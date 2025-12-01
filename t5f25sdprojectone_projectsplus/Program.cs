using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.RegExtension;

namespace t5f25sdprojectone_projectsplus
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Common services
            builder.Services.AddRazorPages();

            // ---------------------------------------------------------
            //// s3 infra
            //// create S3 client (or resolve how you normally do)
            //var region = RegionEndpoint.USEast2;
            //var s3Client = new AmazonS3Client(CredsReader.ReadFromCsv(), region);

            //// run ensure BEFORE registering the dependent service
            //var ensureS3 = new EnsureS3(s3Client, new IaC_ProjectsPlus.EnsureModules.Infralogger(), region.SystemName);            
            //var s3Infra = await ensureS3.EnsureBucketAsync(new EnsureS3Request());

            //// now register the fully-initialized S3BucketService instance
            //var s3Service = new S3BucketService(s3Client, s3Infra); // ctor takes infra
            //builder.Services.AddSingleton(s3Service);

            // register other services and build/run host
            //var app = builder.Build();
            //app.MapGet("/", () => $"Bucket: {s3Service.Options.BucketName}");
            //await app.RunAsync();


            // ----------------------------------------------------------------------------------------////////////////
            // one line: runs EnsureS3, waits for it, registers client and service
            //builder.Services.AddAndInitializeS3bAsync(builder.Configuration).GetAwaiter().GetResult();
            // ---------------------------------------------------------

            //builder.Services.AddAndInitializeDDbAsync(builder.Configuration).GetAwaiter().GetResult();

            //builder.Services.AddAndInitializeRDSAsync(builder.Configuration).GetAwaiter().GetResult();
            // -----------------------------------------------------------------------------------------////////////////

            //github_surface
            // -----------------------------------------------------------------------------------------////////////////

            builder.Services.AddProjectsPlusGitHub(builder.Configuration);
            
            // -----------------------------------------------------------------------------------------////////////////

            // ---------------------------------------------------------
            // Option A: Register DbContext from configuration (recommended)
            // Commented out by request — uncomment to enable.
            // Make sure you have a connection string named "ProjectsPlus"
            // in appsettings.json or your environment.
            // ---------------------------------------------------------
            /*
            builder.Services.AddDbContext<ProjectsPlusDbContext>(opts =>
                opts.UseSqlite(builder.Configuration.GetConnectionString("ProjectsPlus")));

            // Register ProjectsPlus services after DbContext registration
            builder.Services.AddProjectsPlus();
            */

            // ---------------------------------------------------------
            // Option B: Convenience single-call registration
            // Active by default (keeps Program minimal). For demos/local
            // runs you may prefer InMemory; for relational tests replace
            // UseInMemoryDatabase with UseSqlite or UseSqlServer.
            // ---------------------------------------------------------
            builder.Services.AddProjectsPlusWithDb(opts =>
                opts.UseInMemoryDatabase("projectsplus_dev"));

            var app = builder.Build();

            // ----------------------------------------------------------------------------------------////////////////
            //app.MapGet("/s3b", (S3BucketService s3b) => $"Bucket: {s3b.Options.BucketName}");
            //app.MapGet("/ddb", (DynamodbService ddb) => $"Bucket: {ddb.Options.TableName}");
            // ----------------------------------------------------------------------------------------////////////////

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.MapRazorPages();

            app.Run();
            //await app.RunAsync();
        }
    }
}
