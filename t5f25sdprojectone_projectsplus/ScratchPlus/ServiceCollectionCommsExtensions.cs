//// src/ProjectsPlus.Comms/ServiceCollectionExtensions.Comms.cs
//using System;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using StackExchange.Redis;
//using t5f25sdprojectone_projectsplus.Data;
//using t5f25sdprojectone_projectsplus.Services.ComsService;
//using t5f25sdprojectone_projectsplus.Services.ComsService.InMemory;
//using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;

//namespace t5f25sdprojectone_projectsplus.ScratchPlus
//{
//    /// <summary>
//    /// Registers all Communications related services, DbContext and hosted workers.
//    /// Call from your application's composition root:
//    /// services.AddCommunications(Configuration);
//    /// </summary>
//    public static class ServiceCollectionExtensions
//    {
//        /// <summary>
//        /// Registers the communications stack: EF DbContext, presence, notification queue workers,
//        /// template/attachment/notification services and any connection manager implementations.
//        /// The method attempts to be conservative: it reads configuration keys to decide between
//        /// Redis vs in-memory fallbacks and registers hosted services required by the comms subsystem.
//        /// </summary>
//        public static IServiceCollection AddCommunications(this IServiceCollection services, IConfiguration configuration)
//        {
//            if (services == null) throw new ArgumentNullException(nameof(services));
//            if (configuration == null) throw new ArgumentNullException(nameof(configuration));


//            services.AddDbContext<CommsDbContext>(options =>
//            {
//                // Use SQL Server by default; caller can override by replacing the DbContext registration.
//                options.UseSqlServer(commsConn, sql =>
//                {
//                    sql.EnableRetryOnFailure();
//                });
//            });

//            // 2) Redis connection multiplexer (optional)
//            // If a Redis connection string is present, register a shared IConnectionMultiplexer.
//            var redisConn = configuration.GetValue<string>("Comms:Redis:ConnectionString");
//            if (!string.IsNullOrWhiteSpace(redisConn))
//            {
//                services.AddSingleton<IConnectionMultiplexer>(sp =>
//                    ConnectionMultiplexer.Connect(redisConn));
//            }

//            // 3) Presence service selection (Redis-backed or in-memory)
//            var useInMemoryPresence = configuration.GetValue<bool?>("Comms:UseInMemoryPresence") ?? false;
//            if (useInMemoryPresence)
//            {
//                services.AddSingleton<IPresenceService, InMemoryPresenceService>();
//            }
//            else
//            {
//                // If Redis is not configured but in-memory was not explicitly requested, still register in-memory as fallback.
//                if (!string.IsNullOrWhiteSpace(redisConn))
//                {
//                    services.AddSingleton<IPresenceService, RedisPresenceService>();
//                }
//                else
//                {
//                    services.AddSingleton<IPresenceService, InMemoryPresenceService>();
//                }
//            }

//            // 4) Hosted services for presence event persistence and notification dispatch
//            // PresenceEventPersisterBackgroundService requires IConnectionMultiplexer; only register if Redis is available.
//            if (!string.IsNullOrWhiteSpace(redisConn))
//            {
//                services.AddHostedService<PresenceEventPersisterBackgroundService>();
//            }

//            // Notification dispatcher worker - durable queue worker that dequeues notifications and dispatches them.
//            // Register if the implementation exists in the project (recommended).
//            // If you have a NotificationDispatcherBackgroundService implementation, it will be picked up here.
//            // If not present, this registration is harmless (remove or replace with your concrete worker).
//            try
//            {
//                // Defensive: register if the type is available at runtime.
//                var notifWorkerType = typeof(object).Assembly; // no-op to keep compiler happy; actual type referenced below
//            }
//            catch
//            {
//                // ignore
//            }

//            // NOTE: The following registrations assume the corresponding interfaces and implementations
//            // exist in the project. Replace or remove registrations for services you do not have.
//            // 5) Core comms services (template, attachment, notification center, connection manager, chatroom)
//            // Register common service lifetimes:
//            // - AttachmentService: scoped (depends on per-request services like DbContext or external clients)
//            // - TemplateService: singleton or scoped depending on caching strategy (use singleton for in-memory templates)
//            // - NotificationCenter / NotificationService: scoped
//            // - ConnectionManager: singleton (manages connections across the app lifetime)
//            // - ChatroomService: scoped

//            // Example registrations (uncomment/adjust to match your concrete types):
//            // services.AddSingleton<IAttachmentService, AttachmentService>();
//            // services.AddSingleton<ITemplateService, TemplateService>();
//            // services.AddScoped<INotificationService, EfNotificationService>();
//            // services.AddSingleton<IConnectionManager, RedisConnectionManager>();
//            // services.AddScoped<IChatroomService, EfChatroomService>();

//            // 6) Repositories (EF implementations)
//            // Example placeholders - replace with your actual repository interfaces/implementations:
//            // services.AddScoped<IMessageRepository, MessageRepository.Ef>();
//            // services.AddScoped<INotificationRepository, NotificationRepository.Ef>();
//            // services.AddScoped<ITemplateRepository, TemplateRepository.Ef>();
//            // services.AddScoped<ICommAuditRepository, CommAuditRepository.Ef>();
//            // services.AddScoped<IDeadLetterRepository, DeadLetterRepository.Ef>();

//            // 7) Background workers for notifications and queue processing
//            // If you have a NotificationDispatcherBackgroundService implementation, register it here.
//            // If you have a CommQueueWorker (dequeue/retry/dead-letter), register it here.
//            // Example:
//            // services.AddHostedService<NotificationDispatcherBackgroundService>();
//            // services.AddHostedService<CommQueueWorker>();

//            // 8) Misc: health checks, metrics, and options binding
//            // Bind Comms options if present
//            services.Configure<CommsOptions>(configuration.GetSection("Comms"));

//            // Optional: register health checks for Redis and DB if the app uses health checks
//            // services.AddHealthChecks()
//            //     .AddSqlServer(commsConn, name: "comms-sql")
//            //     .AddRedis(redisConn, name: "comms-redis");

//            return services;
//        }
//    }

    
//}
