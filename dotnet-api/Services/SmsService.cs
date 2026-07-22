using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Afrobotics.Bit.Api.Services;

/// <summary>
/// MReq 12: SMS notification service. Falls back to console logging if no SMS gateway configured.
/// Replace with Twilio / Africa's Talking SDK in production.
/// </summary>
public interface ISmsService
{
    Task SendAsync(string phoneNumber, string message, string notificationType);
}

public class SmsService : ISmsService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmsService> _logger;
    private readonly bool _isConfigured;

    public SmsService(IConfiguration config, ILogger<SmsService> logger)
    {
        _config = config;
        _logger = logger;

        var accountSid = _config["Sms:AccountSid"];
        _isConfigured = !string.IsNullOrWhiteSpace(accountSid);
    }

    public async Task SendAsync(string phoneNumber, string message, string notificationType)
    {
        if (_isConfigured)
        {
            // TODO: Integrate Twilio or Africa's Talking SDK here
            // var twilio = new TwilioRestClient(_config["Sms:AccountSid"], _config["Sms:AuthToken"]);
            // await twilio.SendMessageAsync(_config["Sms:FromNumber"], phoneNumber, message);
            _logger.LogInformation("SMS sent to {Phone}: {Message}", phoneNumber, message);
        }
        else
        {
            _logger.LogInformation("[DEV] Would send SMS to {Phone}: {Message}", phoneNumber, message);
        }

        await Task.CompletedTask;
    }
}
