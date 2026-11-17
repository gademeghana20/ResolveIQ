using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ResolveIQ.Application.Abstractions;

namespace ResolveIQ.Tests.Api
{
    /// <summary>One email the app "sent" during a test.</summary>
    public record SentEmail(string To, string Subject, string Body)
    {
        /// <summary>The one-time token inside the password link in this email.</summary>
        public string Token
        {
            get
            {
                var match = Regex.Match(Body, @"[?&]token=([^&\s]+)");
                Assert.True(match.Success, "The email has no token link:\n" + Body);
                return Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        public string Link => Regex.Match(Body, @"https?://\S+").Value;
    }

    /// <summary>
    /// Stands in for the real email sender in tests: nothing is delivered, every email is kept so a test can
    /// read it, exactly as a person would read their inbox. Shared by all tests, so look emails up by recipient.
    /// </summary>
    public class CapturingEmailSender : IEmailSender
    {
        private readonly ConcurrentQueue<SentEmail> _sent = new();

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken = default)
        {
            _sent.Enqueue(new SentEmail(toAddress, subject, body));
            return Task.CompletedTask;
        }

        public IReadOnlyList<SentEmail> To(string address) =>
            _sent.Where(e => string.Equals(e.To, address, StringComparison.OrdinalIgnoreCase)).ToList();

        public SentEmail Latest(string address)
        {
            var emails = To(address);
            Assert.NotEmpty(emails);
            return emails[^1];
        }
    }
}
