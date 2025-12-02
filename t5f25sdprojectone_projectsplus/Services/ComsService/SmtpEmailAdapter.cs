// src/ProjectsPlus.Comms/Channels/SmtpEmailAdapter.cs
using System;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public sealed class SmtpAdapterOptions
    {
        public string Host { get; init; } = "localhost";
        public int Port { get; init; } = 25;
        public bool UseSsl { get; init; } = false;
        public string? Username { get; init; }
        public string? Password { get; init; }
        public string FromAddress { get; init; } = "noreply@example.com";
        public int MaxRetries { get; init; } = 3;
        public int BaseBackoffSeconds { get; init; } = 2;
    }

    public class SmtpEmailAdapter : IChannelAdapter, IDisposable
    {
        private readonly SmtpClient _client;
        private readonly SmtpAdapterOptions _opts;
        private readonly ILogger<SmtpEmailAdapter> _logger;
        private bool _disposed;

        public SmtpEmailAdapter(IOptions<SmtpAdapterOptions> opts, ILogger<SmtpEmailAdapter> logger)
        {
            _opts = opts?.Value ?? throw new ArgumentNullException(nameof(opts));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _client = new SmtpClient(_opts.Host, _opts.Port)
            {
                EnableSsl = _opts.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 100000
            };

            if (!string.IsNullOrWhiteSpace(_opts.Username))
            {
                _client.Credentials = new NetworkCredential(_opts.Username, _opts.Password);
            }
        }

        public async Task<SendResultDto> SendAsync(NotificationSendRequest request, CancellationToken ct = default)
        {
            var attempt = Math.Max(1, request.Attempt);
            var result = new SendResultDto { NotificationId = request.NotificationId, Attempt = attempt };

            var mail = new MailMessage
            {
                From = new MailAddress(_opts.FromAddress),
                Subject = request.Subject ?? string.Empty,
                Body = request.BodyHtml ?? request.Body ?? string.Empty,
                IsBodyHtml = !string.IsNullOrWhiteSpace(request.BodyHtml)
            };
            mail.To.Add(request.Recipient);

            for (int i = 0; i < _opts.MaxRetries; i++)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    await _client.SendMailAsync(mail).ConfigureAwait(false);
                    result.Status = SendStatus.Sent;
                    result.ProviderMessageId = Guid.NewGuid().ToString("D"); // SMTP doesn't return id; generate one
                    result.Timestamp = DateTime.UtcNow;
                    return result;
                }
                catch (SmtpFailedRecipientException ex)
                {
                    _logger.LogWarning(ex, "SMTP failed recipient {Recipient} attempt {Attempt}", request.Recipient, attempt + i);
                    result.ErrorMessage = ex.Message;
                    result.Attempt = attempt + i;
                    // recipient errors are often permanent; break and return failed
                    result.Status = SendStatus.Failed;
                    return result;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger.LogWarning(ex, "SMTP send error attempt {Attempt}", attempt + i);
                    result.ErrorMessage = ex.Message;
                    result.Attempt = attempt + i;
                    // backoff
                    var backoff = TimeSpan.FromSeconds(_opts.BaseBackoffSeconds * Math.Pow(2, i));
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                    continue;
                }
            }

            result.Status = SendStatus.Failed;
            return result;
        }

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
