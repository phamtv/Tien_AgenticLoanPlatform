namespace LoanPlatform.Common.Email;

/// <summary>
/// Sends a notification email. Every service depends only on this
/// interface, not on SmtpClient directly — same DI-abstraction pattern
/// as IEventBus and IKeyVaultService elsewhere in this project.
/// </summary>
public interface IEmailService
{
    Task SendAsync(string subject, string body);
}
