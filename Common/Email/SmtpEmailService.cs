using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LoanPlatform.Common.Email;

/// <summary>
/// Sends email via SMTP using System.Net.Mail.SmtpClient — part of the
/// .NET base class library, not a NuGet package, so this compiles and
/// runs with no external dependency (same reason PBKDF2 was used instead
/// of BCrypt.Net elsewhere in this broader project).
///
/// Worth knowing: Microsoft has marked SmtpClient's async send path
/// obsolete-by-recommendation since .NET 5, suggesting MailKit
/// (a NuGet package) instead — mainly over MailKit's better TLS/auth
/// support for modern mail providers. SmtpClient still works correctly
/// and is used here deliberately, for the same NuGet-access reason
/// documented throughout this project. A production version reaching a
/// real provider (SendGrid, Office 365, etc.) should use MailKit instead
/// — swapping it in is a one-line DI change here, same as everywhere else.
///
/// Configuration comes from IConfiguration (appsettings.json or
/// environment variables), not the key vault abstraction — SMTP settings
/// here are non-secret except the password, which in a real deployment
/// should come through IKeyVaultService the same way JWT secrets do.
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string subject, string body)
    {
        var host = _config["Email:SmtpHost"] ?? "localhost";
        var port = int.Parse(_config["Email:SmtpPort"] ?? "25");
        var from = _config["Email:From"] ?? "loan-platform@example.com";
        var to = _config["Email:To"];
        var enableSsl = bool.Parse(_config["Email:EnableSsl"] ?? "false");
        var username = _config["Email:Username"];
        var password = _config["Email:Password"];

        if (string.IsNullOrWhiteSpace(to))
        {
            // Don't throw — a missing recipient shouldn't take down the
            // business flow that triggered this email. Log loudly instead,
            // the same way a failed vendor call degrades gracefully rather
            // than blocking the whole request elsewhere in this project.
            _logger.LogWarning("Email not sent — Email:To is not configured. Subject was: {Subject}", subject);
            return;
        }

        try
        {
            using var client = new SmtpClient(host, port) { EnableSsl = enableSsl };
            if (!string.IsNullOrEmpty(username))
            {
                client.Credentials = new NetworkCredential(username, password);
            }

            using var message = new MailMessage(from, to, subject, body);
            await client.SendMailAsync(message);

            _logger.LogInformation("Email sent to {Recipient}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            // Same principle as vendor calls and event publishing elsewhere
            // in this project: a downstream notification failing shouldn't
            // fail the actual business operation that already succeeded.
            _logger.LogError(ex, "Failed to send email (Subject: {Subject}) — continuing anyway.", subject);
        }
    }
}
