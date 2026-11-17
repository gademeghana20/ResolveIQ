using Microsoft.Extensions.Logging;
using ResolveIQ.Application.Abstractions;

namespace ResolveIQ.Infrastructure.Services
{
    /// <summary>
    /// The development email "sender": it does not deliver anything, it writes the email to the application log.
    /// That is how you read a reset link while developing, without needing a mail server.
    /// (A real sender, such as SMTP, would implement the same interface and replace this one.)
    /// </summary>
    public class LoggingEmailSender : IEmailSender
    {
        private readonly ILogger<LoggingEmailSender> _logger;

        public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
        {
            _logger = logger;
        }

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "EMAIL (not delivered, development only)\n  To: {To}\n  Subject: {Subject}\n{Body}",
                toAddress,
                subject,
                body);

            return Task.CompletedTask;
        }
    }
}
