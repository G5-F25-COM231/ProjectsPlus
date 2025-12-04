using System.Reflection;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.RegExtension;
using t5f25sdprojectone_projectsplus.Services;
using t5f25sdprojectone_projectsplus.Services.ComsService;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;
using t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter;
using t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter;

namespace t5f25sdprojectone_projectsplus
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Common services
            builder.Services.AddRazorPages();


            // ----------------------------------------------------------------------------------------////////////////
            // one line: runs EnsureS3, EnsureDDB, EnsureRDS, waits for it, registers client and service
            builder.Services.AddAndInitializeS3bAsync(builder.Configuration).GetAwaiter().GetResult();

            builder.Services.AddAndInitializeDDbAsync(builder.Configuration).GetAwaiter().GetResult();

            //builder.Services.AddAndInitializeRDSAsync(builder.Configuration).GetAwaiter().GetResult();
            // -----------------------------------------------------------------------------------------////////////////

            //github_surface       
            builder.Services.AddProjectsPlusGitHub(builder.Configuration);

            // -----------------------------------------------------------------------------------------////////////////

            // ---------------------------------------------------------
            // Option A: Register DbContext from configuration (recommended)
            // Commented out by request — uncomment to enable.
            // Make sure you have a connection string named "ProjectsPlus"
            // in appsettings.json or your environment.
            // ---------------------------------------------------------

            // reflection scan - run before constructing DbContext     

            Console.WriteLine("Scanning assemblies for IReadOnlyCollection<string> properties...");
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().ToList();
            foreach (var asm in assemblies.Distinct())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                    {
                        var pt = p.PropertyType;
                        if (pt?.IsGenericType == true)
                        {
                            var def = pt.GetGenericTypeDefinition();
                            var arg = pt.GetGenericArguments().FirstOrDefault();
                            if ((def == typeof(IReadOnlyCollection<>) || def == typeof(IReadOnlyList<>)) && arg == typeof(string))
                                Console.WriteLine($"Found property: {t.FullName}.{p.Name}  Type: {pt.FullName}");
                        }
                    }
                }
            }
            Console.WriteLine("Scan complete.");


            // DB Context (expect connection string in appsettings.Development.json under ConnectionStrings:DefaultConnection)
            var mssqlConnStr = builder.Configuration.GetConnectionString("ProjectsPlus");
            builder.Services.AddDbContext<ProjectsPlusDbContext>(opts => opts.UseSqlServer(mssqlConnStr));


            //builder.Services.AddDbContext<ProjectsPlusDbContext>(opts =>
            //    opts.UseSqlite(builder.Configuration.GetConnectionString("ProjectsPlus")));

            //builder.Services.AddDbContext<ProjectsPlusDbContext>(options =>
            //{
            //    // Use SQL Server by default; caller can override by replacing the DbContext registration.  
            //    options.UseSqlServer(mssqlConnStr, sql =>
            //    {
            //        sql.EnableRetryOnFailure();
            //    });
            //});

            // ----------------------------------------------------------------------------------------////////////////

            // Register ProjectsPlus services after DbContext registration
            builder.Services.AddProjectsPlus();
            // change from AddScoped to AddSingleton

            builder.Services.AddCommunications(builder.Configuration);

            builder.Services.AddDdbRComms(builder.Configuration);
            builder.Services.AddSqlRComms();

            // feature-flagged registration example
            //if (configuration.GetValue<bool>("UseSqlRedisAdapter"))
            //    services.AddSingleton<IRedisClient>(sp => new MssqlAdapter(sp, pollInterval: TimeSpan.FromSeconds(1)));
            //else
            //    services.AddSingleton<IRedisClient, RedisClient>(); // existing client

            // ----------------------------------------------------------------------------------------////////////////

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

            app.MapGet("/s3b", (S3BucketService s3b) => $"Bucket: {s3b.Options.BucketName}");
            app.MapGet("/ddb", (DynamodbService ddb) => $"Bucket: {ddb.Options.TableName}");

            // ----------------------------------------------------------------------------------------////////////////
            // -- Ensure msgc and Seed ------------------------------------------------------
            using (var scope = app.Services.CreateScope())
            {
                var svc = scope.ServiceProvider;
                var msgc = svc.GetRequiredService<IMessageCenter>();
                var chro = svc.GetRequiredService<IChatroomService>();
                app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
                app.UseMiddleware<WebSocketHandlerMiddleware>(msgc, chro);
            }

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


            // -- Ensure DB and Seed ------------------------------------------------------
            using (var scope = app.Services.CreateScope())
            {
                var svc = scope.ServiceProvider;
                var db = svc.GetRequiredService<ProjectsPlusDbContext>();
                // Ensure DB exists (dev) and run seeder

                db.Database.EnsureCreated();
                //await DbSeeder.SeedAsync(db);
            }

            //await app.RunAsync();
            app.Run();
        }
    }
}
