// src/ProjectsPlus.Comms/Channels/SmtpEmailSender.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    /// <summary>
    /// SMTP-backed IEmailSender using MailKit/MimeKit.
    /// - Supports attachments from EmailDto.Attachments (filename -> bytes)
    /// - Honors CancellationToken for async SMTP operations
    /// - Returns a SendResultDto with ProviderMessageId set to the MIME Message-Id when available
    /// </summary>
    public class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpOptions _opts;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IOptions<SmtpOptions> opts, ILogger<SmtpEmailSender> logger)
        {
            _opts = opts?.Value ?? throw new ArgumentNullException(nameof(opts));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<SendResultDto> SendEmailAsync(EmailDto email, CancellationToken ct = default)
        {
            if (email == null) throw new ArgumentNullException(nameof(email));
            var result = new SendResultDto
            {
                NotificationId = Guid.Empty,
                Status = SendStatus.Failed,
                Timestamp = DateTime.UtcNow,
                Attempt = 1
            };

            var message = new MimeMessage();

            try
            {
                // From
                var from = !string.IsNullOrWhiteSpace(_opts.DefaultFromAddress) ? _opts.DefaultFromAddress : email.From;
                if (string.IsNullOrWhiteSpace(from))
                    throw new InvalidOperationException("No from address configured or provided.");

                message.From.Add(MailboxAddress.Parse(from));

                // To
                if (string.IsNullOrWhiteSpace(email.To))
                    throw new ArgumentException("Email.To is required", nameof(email));

                message.To.Add(MailboxAddress.Parse(email.To));

                // Optional display name
                if (!string.IsNullOrWhiteSpace(email.ToName))
                {
                    // replace the To with a named mailbox if provided
                    message.To.Clear();
                    message.To.Add(new MailboxAddress(email.ToName, email.To));
                }

                // Subject
                message.Subject = email.Subject ?? string.Empty;

                // Build body (html preferred, fallback to text)
                var bodyBuilder = new BodyBuilder();

                if (!string.IsNullOrWhiteSpace(email.BodyHtml))
                    bodyBuilder.HtmlBody = email.BodyHtml;

                if (!string.IsNullOrWhiteSpace(email.BodyText))
                    bodyBuilder.TextBody = email.BodyText;
                else if (string.IsNullOrWhiteSpace(bodyBuilder.TextBody) && !string.IsNullOrWhiteSpace(email.BodyHtml))
                    bodyBuilder.TextBody = StripHtml(email.BodyHtml);

                // Attachments (filename -> bytes)
                if (email.Attachments != null)
                {
                    foreach (var kv in email.Attachments)
                    {
                        var filename = kv.Key ?? Guid.NewGuid().ToString("N");
                        var bytes = kv.Value;
                        if (bytes == null) continue;
                        bodyBuilder.Attachments.Add(filename, bytes);
                    }
                }

                message.Body = bodyBuilder.ToMessageBody();

                // Headers
                if (email.Headers != null)
                {
                    foreach (var h in email.Headers)
                    {
                        if (!string.IsNullOrWhiteSpace(h.Key) && !string.IsNullOrWhiteSpace(h.Value))
                            message.Headers[h.Key] = h.Value;
                    }
                }

                // Send via MailKit
                using var client = new SmtpClient();

                // Configure timeouts if provided
                if (_opts.SocketTimeout.HasValue)
                    client.Timeout = (int)_opts.SocketTimeout.Value.TotalMilliseconds;

                SecureSocketOptions secure = _opts.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
                if (_opts.ForceStartTls) secure = SecureSocketOptions.StartTls;

                // Connect
                await client.ConnectAsync(_opts.Host, _opts.Port, secure, ct).ConfigureAwait(false);

                // Authenticate if credentials provided
                if (!string.IsNullOrWhiteSpace(_opts.Username))
                {
                    await client.AuthenticateAsync(_opts.Username, _opts.Password ?? string.Empty, ct).ConfigureAwait(false);
                }

                // Send
                await client.SendAsync(message, ct).ConfigureAwait(false);

                // Disconnect gracefully
                await client.DisconnectAsync(true, ct).ConfigureAwait(false);

                // Success
                result.Status = SendStatus.Sent;
                result.ProviderMessageId = message.MessageId;
                result.ErrorMessage = null;
                result.Timestamp = DateTime.UtcNow;
                return result;
            }
            catch (OperationCanceledException oce)
            {
                _logger.LogInformation(oce, "SMTP send cancelled");
                result.Status = SendStatus.Failed;
                result.ErrorMessage = "Cancelled";
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMTP send failed to {To}", email.To);
                result.Status = SendStatus.Failed;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var arr = new char[html.Length];
            var idx = 0;
            var inside = false;
            foreach (var ch in html)
            {
                if (ch == '<') { inside = true; continue; }
                if (ch == '>') { inside = false; continue; }
                if (!inside) arr[idx++] = ch;
            }
            return new string(arr, 0, idx).Trim();
        }
    }

    /// <summary>
    /// Options for SMTP sender. Bind from configuration section "Comms:Smtp" or similar.
    /// </summary>
    public class SmtpOptions
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 25;
        public bool UseSsl { get; set; } = false;
        public bool ForceStartTls { get; set; } = false;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? DefaultFromAddress { get; set; }
        public TimeSpan? SocketTimeout { get; set; }
    }
}
