using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Tests.Api
{
    /// <summary>
    /// Starts the real API in memory for tests. It gets its own throwaway SQL Server database (LocalDB on your computer)
    /// (created from the real migrations, deleted afterwards), so tests never touch your real data.
    /// The app's clock is a FakeTimeProvider, so tests can move time forward at will.
    /// </summary>
    public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        public const string BootstrapAdminEmail = "bootstrap.admin@example.com";
        public const string BootstrapAdminPassword = "Boot#Strap123";
        public const string StandardPassword = "Test#Pass123";

        private const string JwtKey = "integration-test-signing-key-that-is-comfortably-long-1234567890";

        public string DatabaseName { get; } = $"ResolveIQ_Tests_{Guid.NewGuid():N}";

        /// <summary>The app's clock. Starts at the real current time; tests call Advance to move it.</summary>
        public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

        /// <summary>Every email the app "sent" (nothing is really delivered in tests).</summary>
        public CapturingEmailSender Emails { get; } = new();

        /// <summary>Whether the app is told to create a first Admin at startup.</summary>
        protected virtual bool SeedBootstrapAdmin => true;

        /// <summary>
        /// Which SQL Server the throwaway databases are made on. By default your computer's LocalDB. A build server
        /// (which has no LocalDB) sets RESOLVEIQ_TEST_SQL_SERVER to the server part of a connection string instead,
        /// for example "Server=localhost,1433;User Id=sa;Password=...".
        /// </summary>
        private const string ServerVariable = "RESOLVEIQ_TEST_SQL_SERVER";

        private string ConnectionString
        {
            get
            {
                var server = Environment.GetEnvironmentVariable(ServerVariable);
                if (string.IsNullOrWhiteSpace(server))
                {
                    server = "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True";
                }

                return $"{server};Database={DatabaseName};MultipleActiveResultSets=true;TrustServerCertificate=True";
            }
        }

        /// <summary>
        /// Extra settings for a test that needs the app set up differently (registration closed, a low rate limit...).
        /// A subclass overrides this; every other test gets the normal app.
        /// </summary>
        protected virtual IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            // UseSetting is applied early enough for Program.cs to see these values at startup.
            builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
            builder.UseSetting("Jwt:Key", JwtKey);

            // Tests make thousands of calls from "one address" in a few minutes, so the limits are lifted, and the
            // background housekeeping is off (tests run each job directly instead). Tests of those features override these.
            builder.UseSetting("RateLimiting:AuthPermitLimit", "1000000");
            builder.UseSetting("RateLimiting:GeneralPermitLimit", "1000000");
            builder.UseSetting("Maintenance:Enabled", "false");

            foreach (var (key, value) in ExtraSettings)
            {
                builder.UseSetting(key, value);
            }

            if (SeedBootstrapAdmin)
            {
                builder.UseSetting("Seed:AdminEmail", BootstrapAdminEmail);
                builder.UseSetting("Seed:AdminPassword", BootstrapAdminPassword);
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Time);

                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Emails);
            });
        }

        public async Task InitializeAsync()
        {
            await using var db = NewDbContext();
            await db.Database.MigrateAsync();
        }

        // Explicit, because WebApplicationFactory already has its own DisposeAsync.
        async Task IAsyncLifetime.DisposeAsync()
        {
            await base.DisposeAsync();

            await using var db = NewDbContext();
            await db.Database.EnsureDeletedAsync();
        }

        private ApplicationDbContext NewDbContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);
    }

    /// <summary>The same API, but told NOT to create a first Admin, for testing the bootstrap itself.</summary>
    public class NoAdminApiFactory : ApiFactory
    {
        protected override bool SeedBootstrapAdmin => false;
    }

    [CollectionDefinition(Name)]
    public class ApiCollection : ICollectionFixture<ApiFactory>
    {
        public const string Name = "Api";
    }
}
