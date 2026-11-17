namespace ResolveIQ.Application.Abstractions
{
    /// <summary>
    /// Sends a plain-text email. The app only knows this interface, so the way mail is really delivered
    /// (today: written to the log for development; later: an SMTP server or an email service) can be swapped
    /// without touching the code that decides what to send.
    /// </summary>
    public interface IEmailSender
    {
        Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default);
    }
}
