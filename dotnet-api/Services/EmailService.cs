using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// MReq 12, 15: Sends email notifications via SMTP and logs every notification to the database.
/// Falls back gracefully if SMTP is not configured — logs a warning instead of throwing.
/// </summary>
public interface IEmailService
{
    Task SendAsync(string toEmail, string subject, string body, string notificationType);
}

public class EmailService : IEmailService
{
    private readonly PostgresDbContext _context;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly bool _isConfigured;

    public EmailService(PostgresDbContext context, IConfiguration config, ILogger<EmailService> logger)
    {
        _context = context;
        _config = config;
        _logger = logger;

        var host = _config["Smtp:Host"];
        _isConfigured = !string.IsNullOrWhiteSpace(host);
    }

    public async Task SendAsync(string toEmail, string subject, string body, string notificationType)
    {
        var status = "Sent";

        if (_isConfigured)
        {
            try
            {
                using var smtp = new SmtpClient
                {
                    Host = _config["Smtp:Host"]!,
                    Port = int.Parse(_config["Smtp:Port"] ?? "587"),
                    EnableSsl = true,
                    Credentials = new NetworkCredential(
                        _config["Smtp:User"],
                        _config["Smtp:Password"])
                };

                var fromEmail = _config["Smtp:FromEmail"] ?? "noreply@afrobotics.co.za";
                using var message = new MailMessage(fromEmail, toEmail, subject, body)
                {
                    IsBodyHtml = false
                };

                await smtp.SendMailAsync(message);
                _logger.LogInformation("Email sent to {Recipient}: {Subject}", toEmail, subject);
            }
            catch (Exception ex)
            {
                status = "Failed";
                _logger.LogWarning(ex, "SMTP send failed to {Recipient}: {Subject}", toEmail, subject);
            }
        }
        else
        {
            // Development / no SMTP configured — log only
            _logger.LogInformation("[DEV] Would send email to {Recipient}: {Subject}\n{Body}", toEmail, subject, body);
        }

        // Always persist the notification record
        try
        {
            var notification = new NotificationItem
            {
                RecipientEmail = toEmail,
                Type = notificationType,
                Subject = subject,
                Body = body,
                Status = status
            };
            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist notification record for {Recipient}", toEmail);
        }
    }
}
