using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Services.ComsService;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches
{
    public sealed class RedisSmsSender : ISmsSender
    {
        private readonly IRedisClient _redis;
        private readonly ILogger<RedisSmsSender> _log;
        private const string Channel = "outgoing:sms";

        public RedisSmsSender(IRedisClient redis, ILogger<RedisSmsSender> log)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task<SendResultDto> SendSmsAsync(SmsDto sms, CancellationToken ct = default)
        {
            if (sms == null) throw new ArgumentNullException(nameof(sms));
            if (string.IsNullOrWhiteSpace(sms.ToPhone))
            {
                return new SendResultDto
                {
                    NotificationId = Guid.NewGuid(),
                    Status = SendStatus.Failed,
                    ErrorMessage = "Missing recipient phone"
                };
            }

            var notificationId = Guid.NewGuid();
            var payload = new
            {
                Id = notificationId,
                To = sms.ToPhone,
                From = sms.FromPhone,
                sms.Body
            };

            try
            {
                var json = JsonSerializer.Serialize(payload);
                // assume PublishAsync returns Task<long> or Task; await regardless
                await _redis.PublishAsync(Channel, json, ct).ConfigureAwait(false);

                return new SendResultDto
                {
                    NotificationId = notificationId,
                    Status = SendStatus.Sent,
                    ProviderMessageId = null,
                    ErrorMessage = null,
                    Timestamp = DateTime.UtcNow,
                    Attempt = 1
                };
            }
            catch (OperationCanceledException)
            {
                _log.LogWarning("SendSmsAsync cancelled for {NotificationId}", notificationId);
                return new SendResultDto
                {
                    NotificationId = notificationId,
                    Status = SendStatus.Failed,
                    ErrorMessage = "Cancelled",
                    Timestamp = DateTime.UtcNow,
                    Attempt = 1
                };
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to publish SMS {NotificationId}", notificationId);
                return new SendResultDto
                {
                    NotificationId = notificationId,
                    Status = SendStatus.Failed,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow,
                    Attempt = 1
                };
            }
        }
    }

    public sealed class RedisPushSender : IPushSender
    {
        private readonly IRedisClient _redis;
        private readonly ILogger<RedisPushSender> _log;
        private const string Channel = "outgoing:push";

        public RedisPushSender(IRedisClient redis, ILogger<RedisPushSender> log)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task<SendResultDto> SendPushAsync(PushDto push, CancellationToken ct = default)
        {
            if (push == null) throw new ArgumentNullException(nameof(push));
            if (string.IsNullOrWhiteSpace(push.DeviceToken))
            {
                return new SendResultDto
                {
                    NotificationId = Guid.NewGuid(),
                    Status = SendStatus.Failed,
                    ErrorMessage = "Missing device token"
                };
            }

            var notificationId = Guid.NewGuid();
            var payload = new
            {
                Id = notificationId,
                Token = push.DeviceToken,
                push.Title,
                push.Body,
                push.Data
            };

            try
            {
                var json = JsonSerializer.Serialize(payload);
                await _redis.PublishAsync(Channel, json, ct).ConfigureAwait(false);

                return new SendResultDto
                {
                    NotificationId = notificationId,
                    Status = SendStatus.Sent,
                    ProviderMessageId = null,
                    ErrorMessage = null,
                    Timestamp = DateTime.UtcNow,
                    Attempt = 1
                };
            }
            catch (OperationCanceledException)
            {
                _log.LogWarning("SendPushAsync cancelled for {NotificationId}", notificationId);
                return new SendResultDto
                {
                    NotificationId = notificationId,
                    Status = SendStatus.Failed,
                    ErrorMessage = "Cancelled",
                    Timestamp = DateTime.UtcNow,
                    Attempt = 1
                };
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to publish Push {NotificationId}", notificationId);
                return new SendResultDto
                {
                    NotificationId = notificationId,
                    Status = SendStatus.Failed,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow,
                    Attempt = 1
                };
            }
        }
    }
}
