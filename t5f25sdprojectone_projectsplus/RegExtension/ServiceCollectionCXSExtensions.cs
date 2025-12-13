// ServiceCollectionDynamoExtensions.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using t5f25sdprojectone_projectsplus.Services;
using t5f25sdprojectone_projectsplus.Services.ComsService;
using t5f25sdprojectone_projectsplus.Services.ComsService.InMemory;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;
using t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches;
using t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter;
using t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;
using ICommQueue = t5f25sdprojectone_projectsplus.Services.ComsService.ICommQueue;
using IMessageRepository = t5f25sdprojectone_projectsplus.Services.ComsService.IMessageRepository;
using INotificationRepository = t5f25sdprojectone_projectsplus.Services.ComsService.INotificationRepository;


namespace t5f25sdprojectone_projectsplus.RegExtension
{
    public static class ServiceCollectionCXSExtensions
    {
        /// <summary>
        /// Ensures Communications exists, then registers the initialized.
        /// Call and await this BEFORE calling builder.Build()/host.RunAsync().
        /// </summary>
        public static IServiceCollection AddCommunications(
            this IServiceCollection services,
            IConfiguration configuration,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            //---------------------------------------------------/////////////////////////////
            // For dev/tests
            services.AddSingleton<IChatroomService, InMemoryChatroomService>();

            //---------------------------------------------------/////////////////////////////

            services.AddScoped<IAttachmentService, AttachmentService>();
            //services.AddScoped<INotificationCenter, NotificationCenter>(); // or AddScoped/AddTransient as appropriate

       
            //---------------------------------------------------/////////////////////////////


            // in ServiceCollectionExtensions.Comms.cs or Startup
            services.Configure<SmtpAdapterOptions>(configuration.GetSection("Smtp"));
            services.Configure<WebhookAdapterOptions>(configuration.GetSection("WebhookAdapter"));

            services.AddSingleton<IChannelAdapter, NoopAdapter>(); // default fallback

            // Register SMTP adapter as named/typed adapter
            services.AddTransient<IEmailSender, SmtpEmailSender>(); // or AddScoped/AddSingleton as appropriate
            services.AddSingleton<SmtpEmailAdapter>();

            services.AddSingleton<IChannelAdapter>(sp => sp.GetRequiredService<SmtpEmailAdapter>());

            // Register webhook adapter with HttpClientFactory
            services.AddHttpClient<WebhookAdapter>()
                .ConfigureHttpClient((sp, client) =>
                {
                    var opts = sp.GetRequiredService<IOptions<WebhookAdapterOptions>>().Value;
                    client.Timeout = opts.Timeout;
                });
            services.AddSingleton<IChannelAdapter>(sp => sp.GetRequiredService<WebhookAdapter>());

            // Alternatively register adapters by channel type in a factory or dictionary for NotificationCenter to resolve.


            //---------------------------------------------------/////////////////////////////

            //var mssqlConn = configuration.GetConnectionString("ProjectsPlus");
            //services.AddDbContext<ProjectsPlusDbContext>(options =>
            //{
            //    // Use SQL Server by default; caller can override by replacing the DbContext registration.  
            //    options.UseSqlServer(mssqlConn, sql =>
            //    {
            //        sql.EnableRetryOnFailure();
            //    });
            //});

            //---------------------------------------------------/////////////////////////////

            // 2) Redis connection multiplexer (optional)
            // If a Redis connection string is present, register a shared IConnectionMultiplexer.
            //var redisConn = configuration.GetValue<string>("Comms:Redis:ConnectionString");
            //if (!string.IsNullOrWhiteSpace(redisConn))
            //{
            //    services.AddSingleton<IConnectionMultiplexer>(sp =>
            //        ConnectionMultiplexer.Connect(redisConn));
            //}

            // read flags and connection string from configuration
            var useSqlAdapter = configuration.GetValue<bool>("Comms:UseSqlRedisAdapter");
            var useDdbAdapter = configuration.GetValue<bool>("Comms:UseDdbRedisAdapter");
            var redisConn = configuration.GetValue<string>("Comms:Redis:ConnectionString");

            var useAdabpter = useSqlAdapter || useDdbAdapter;
            Console.WriteLine("------------------> This is the test -------------> : " + useAdabpter);//----------------------------
            if (useAdabpter)
            {
                if (useSqlAdapter)
                {
                    // Use the SQL-backed adapter as the IRedisClient implementation
                    services.AddSingleton<IRedisClient>(sp => new MssqlAdapter(sp, pollInterval: TimeSpan.FromSeconds(1)));
                }
                else if (useDdbAdapter)
                {
                    // Use the ddb-backed adapter as the IRedisClient implementation
                    services.AddSingleton<IRedisClient>(sp => new DdbAdapter(sp, pollInterval: TimeSpan.FromSeconds(1)));
                }
            }
            else
            {
                // If a Redis connection string exists, register a shared multiplexer and a thin wrapper
                if (!string.IsNullOrWhiteSpace(redisConn))
                {
                    services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(redisConn));

                    // Register a small adapter that implements IRedisClient using the multiplexer
                    services.AddSingleton<IRedisClient>(sp =>
                    {
                        var mux = sp.GetRequiredService<IConnectionMultiplexer>();
                        return new StackExchangeRedisClient(mux); // see minimal wrapper below
                    });
                }
                else
                {
                    // No Redis configured and not using SQL adapter -> fail fast or register a no-op/test impl
                    throw new InvalidOperationException("No Redis connection string and UseSqlRedisAdapter is false.");
                }
            }


            //---------------------------------------------------/////////////////////////////

            // 3) Presence service selection (Redis-backed or in-memory)
            var useInMemoryPresence = configuration.GetValue<bool?>("Comms:UseInMemoryPresence") ?? false;
            if (useInMemoryPresence)
            {
                services.AddSingleton<IPresenceService, InMemoryPresenceService>();
            }
            else
            {
                // If Redis is not configured but in-memory was not explicitly requested, still register in-memory as fallback.
                if (!string.IsNullOrWhiteSpace(redisConn))
                {
                    services.AddSingleton<IPresenceService, RedisPresenceService>();
                }
                else
                {
                    services.AddSingleton<IPresenceService, InMemoryPresenceService>();
                }
            }

            // 4) Hosted services for presence event persistence and notification dispatch
            // PresenceEventPersisterBackgroundService requires IConnectionMultiplexer; only register if Redis is available.
            if (!string.IsNullOrWhiteSpace(redisConn))
            {
                services.AddHostedService<PresenceEventPersisterBackgroundService>();
            }

            // Notification dispatcher worker - durable queue worker that dequeues notifications and dispatches them.
            // Register if the implementation exists in the project (recommended).
            // If you have a NotificationDispatcherBackgroundService implementation, it will be picked up here.
            // If not present, this registration is harmless (remove or replace with your concrete worker).
            try
            {
                // Defensive: register if the type is available at runtime.
                var notifWorkerType = typeof(object).Assembly; // no-op to keep compiler happy; actual type referenced below
            }
            catch
            {
                // ignore
            }

            //---------------------------------------------------/////////////////////////////

            // in Startup or ServiceCollectionExtensions.Comms.cs
            services.AddSingleton<CommQueueWorkerOptions>();
            services.Configure<CommQueueWorkerOptions>(cfg =>
            {
                cfg.MaxBatchSize = configuration.GetValue<int>("CommQueueWorker:MaxBatchSize", 10);
                cfg.PollDelay = TimeSpan.FromSeconds(configuration.GetValue<int>("CommQueueWorker:PollDelaySeconds", 2));
                cfg.MaxAttempts = configuration.GetValue<int>("CommQueueWorker:MaxAttempts", 5);
                cfg.MaxBackoffMultiplier = configuration.GetValue<int>("CommQueueWorker:MaxBackoffMultiplier", 8);
                cfg.UseJitter = configuration.GetValue<bool>("CommQueueWorker:UseJitter", true);
            });

            //services.Configure<CommQueueWorkerOptions>(configuration.GetSection("CommQueueWorker"));
            //services.AddHostedService<CommQueueWorker>();

            //services.AddScoped<ITemplateRepository, TemplateRepository>();
            services.AddScoped<ITemplateService, TemplateService>();

            //---------------------------------------------------/////////////////////////////

            // 6) Repositories (EF implementations)
            // Example placeholders - replace with your actual repository interfaces/implementations:
            services.AddScoped<IMessageRepository, MessageRepository>();
            services.AddScoped<INotificationRepository, NotificationRepository>();
            services.AddScoped<Services.ComsService.Repositories.ITemplateRepository, TemplateRepository>();
            //services.AddScoped<Services.ComsService.ITemplateRepository, TemplateRepository>();
            services.AddScoped<ICommAuditRepository, CommAuditRepository>();
            services.AddScoped<IDeadLetterRepository, DeadLetterRepository>();

            //services.AddScoped<ITemplateService, TemplateService>();
            //services.AddScoped<INotificationRepository, NotificationRepository>();
            //services.AddScoped<INotificationCenter, NotificationCenter>(); // make NotificationCenter scoped


            // 7) Background workers for notifications and queue processing
            // If you have a NotificationDispatcherBackgroundService implementation, register it here.
            // If you have a CommQueueWorker (dequeue/retry/dead-letter), register it here.
            // Example:
            services.AddHostedService<NotificationDispatcherBackgroundService>();
            services.AddScoped<ICommQueue, NotificationRepository>();
            

            // 8) Misc: health checks, metrics, and options binding
            // Bind Comms options if present
            services.Configure<CommsOptions>(configuration.GetSection("Comms"));

            // Optional: register health checks for Redis and DB if the app uses health checks
            //services.AddHealthChecks()
            //    .AddSqlServer(mssqlConn, name: "comms-sql")
            //    .AddRedis(redisConn, name: "comms-redis");


            //---------------------------------------------------/////////////////////////////

            // register Redis-backed connection manager
            services.AddSingleton<RedisConnectionManagerOptions>();
            services.AddSingleton(sp =>
            {
                var opts = new RedisConnectionManagerOptions
                {
                    RedisConfiguration = configuration.GetValue<string>("Redis:Connection", "localhost:6379"),
                    InstanceId = configuration.GetValue<string>("Redis:InstanceId", Environment.MachineName),
                    Prefix = configuration.GetValue<string>("Redis:Prefix", "comms:")
                };
                var logger = sp.GetRequiredService<ILogger<RedisConnectionManager>>();
                return new RedisConnectionManager(opts, logger);
            });

            // ensure middleware uses the IConnectionManager abstraction
            services.AddSingleton<IConnectionManager>(sp => sp.GetRequiredService<RedisConnectionManager>());

            // NOTE: The following registrations assume the corresponding interfaces and implementations
            // exist in the project. Replace or remove registrations for services you do not have.
            // 5) Core comms services (template, attachment, notification center, connection manager, chatroom)
            // Register common service lifetimes:
            // - AttachmentService: scoped (depends on per-request services like DbContext or external clients)
            // - TemplateService: singleton or scoped depending on caching strategy (use singleton for in-memory templates)
            // - NotificationCenter / NotificationService: scoped
            // - ConnectionManager: singleton (manages connections across the app lifetime)
            // - ChatroomService: scoped

            // Example registrations (uncomment/adjust to match your concrete types):            
            //services.AddSingleton<ITemplateService, TemplateService>();
            services.AddScoped<ITemplateService, TemplateService>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddSingleton<IConnectionManager, RedisConnectionManager>();
            services.AddScoped<IChatroomService, ChatroomService>();
            services.AddScoped<IMessageCenter, MessageCenter>();

            //---------------------------------------------------/////////////////////////////

            // in ServiceCollectionExtensions.Comms.cs or Startup
            services.AddSingleton<IConnectionManager, ConnectionManager>();

            // WebSocket middleware options and registration
            var wsOptions = new WebSocketHandlerOptions
            {
                Path = "/ws",
                ReceiveBufferSize = 4 * 1024,
                KeepAliveInterval = TimeSpan.FromSeconds(30),
                AuthenticateAsync = async ctx =>
                {
                    // Example: read bearer token and validate; return userId or null
                    var auth = ctx.Request.Headers["Authorization"].ToString();
                    if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer "))
                    {
                        var token = auth.Substring("Bearer ".Length).Trim();
                        // validate token and map to user id
                        //return Guid ? userId;
                        try
                        {
                            var keyString = configuration["Jwt:Key"];
                            if (string.IsNullOrEmpty(keyString)) return null;

                            var key = Encoding.UTF8.GetBytes(keyString);
                            var tokenHandler = new JwtSecurityTokenHandler();
                            var validationParameters = new TokenValidationParameters
                            {
                                ValidateIssuerSigningKey = true,
                                IssuerSigningKey = new SymmetricSecurityKey(key),
                                ValidateIssuer = !string.IsNullOrEmpty(configuration["Jwt:Issuer"]),
                                ValidIssuer = configuration["Jwt:Issuer"],
                                ValidateAudience = !string.IsNullOrEmpty(configuration["Jwt:Audience"]),
                                ValidAudience = configuration["Jwt:Audience"],
                                ValidateLifetime = true,
                                ClockSkew = TimeSpan.FromMinutes(2)
                            };

                            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

                            var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                      ?? principal.FindFirst("sub")?.Value
                                      ?? principal.FindFirst("uid")?.Value
                                      ?? principal.FindFirst("userId")?.Value;

                            if (!string.IsNullOrEmpty(sub) && long.TryParse(sub, out var userId))
                            {
                                return userId;
                            }
                        }
                        catch (SecurityTokenException)
                        {
                            return null;
                        }
                        catch
                        {
                            return null;
                        }
                    }
                    return null;
                }
            };

            services.AddSingleton(wsOptions);

            // Register middleware dependencies
            //services.AddSingleton<WebSocketHandlerMiddleware>(); // -----> add to pipeline in program.cs
            // Register IMessageCenter and IChatroomService implementations
            services.AddScoped<IMessageCenter, MessageCenter>(); // or your concrete
            services.AddScoped<IChatroomService, ChatroomService>(); // or your concrete

            services.AddTransient<IWebhookSender, WebhookSender>();

            services.AddScoped<ICommAuditStore, CommAuditStore>(); // or AddSingleton/AddTransient as appropriate

            //---------------------------------------------------/////////////////////////////

            services.AddScoped<INotificationService, NotificationService>();
            services.AddSingleton<INotificationProvider, WebhookNotificationProvider>(); // add others as needed
            services.AddHostedService<NotificationDispatcherBackgroundService>();

            // in Startup or ServiceCollectionExtensions              
            services.AddSingleton<INotificationProvider, InAppNotificationProvider>();

            // register providers array for dispatcher
            services.AddSingleton(provider =>
                provider.GetServices<INotificationProvider>().ToArray()
            );

            //---------------------------------------------------/////////////////////////////

            // ServiceCollection registration
            //var rconnStr = configuration["Redis:Connection"]; // yet to be defined
            var rconnStr = configuration.GetConnectionString("Redis")
               ?? configuration.GetValue<string>("Redis:Connection")
               ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION");

            if (string.IsNullOrWhiteSpace(rconnStr))
            {
                throw new InvalidOperationException("Redis connection string not configured. Set ConnectionStrings:Redis or Redis:Connection or REDIS_CONNECTION.");
            }

            services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(rconnStr));
            services.AddSingleton<IPresenceService, RedisPresenceService>();
            services.AddHostedService<PresenceEventPersisterBackgroundService>();           

            services.AddScoped<ISmsSender, RedisSmsSender>();
            services.AddScoped<IPushSender, RedisPushSender>();
            services.AddScoped<NotificationCenter>();
            services.AddScoped<INotificationCenter, NotificationCenter>();
            services.AddHostedService<CommQueueWorker>();

            return services;
        }
    }

    /// <summary>
    /// Lightweight options bag for communications subsystem. Extend as needed.
    /// </summary>
    public class CommsOptions
    {
        public bool UseInMemoryPresence { get; set; } = false;
        public RedisOptions? Redis { get; set; }
    }

    public class RedisOptions
    {
        public string? ConnectionString { get; set; }
    }
}
