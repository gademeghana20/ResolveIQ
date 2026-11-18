using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResolveIQ.Domain.Enums;
using ResolveIQ.Domain.Sla;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Tests.Api
{
    /// <summary>The optional made-up demo data for a fresh Development database.</summary>
    public class DemoDataTests : IClassFixture<NoAdminApiFactory>
    {
        private readonly NoAdminApiFactory _factory;

        public DemoDataTests(NoAdminApiFactory factory) => _factory = factory;

        [Fact]
        public async Task FillsAnEmptyDatabaseWithABelievableSetOfIncidents_AndOnlyOnce()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

            // Without the three basic demo people it does nothing rather than half-fill.
            Assert.False(await DemoDataSeeder.SeedAsync(db, userManager, ApiFactory.StandardPassword));
            Assert.Empty(await db.Incidents.ToListAsync());

            await IdentitySeeder.SeedDemoUsersAsync(userManager, ApiFactory.StandardPassword);
            Assert.True(await DemoDataSeeder.SeedAsync(db, userManager, ApiFactory.StandardPassword));

            var incidents = await db.Incidents.AsNoTracking().ToListAsync();
            Assert.Equal(14, incidents.Count);
            Assert.Equal(incidents.Count, incidents.Select(i => i.IncidentNumber).Distinct().Count());

            // Every status is shown at least once.
            foreach (var status in Enum.GetValues<IncidentStatus>())
            {
                Assert.Contains(incidents, i => i.Status == status);
            }

            // Each incident tells a consistent story.
            var now = DateTime.UtcNow;
            foreach (var incident in incidents)
            {
                Assert.True(incident.CreatedAtUtc < now);
                Assert.NotNull(await db.Categories.FindAsync(incident.CategoryId));

                if (incident.Status is IncidentStatus.Resolved or IncidentStatus.Closed)
                {
                    Assert.False(string.IsNullOrWhiteSpace(incident.ResolutionNotes));
                    Assert.True(incident.ResolvedAtUtc > incident.CreatedAtUtc);
                }

                if (incident.Status == IncidentStatus.Closed)
                {
                    Assert.True(incident.ClosedAtUtc > incident.ResolvedAtUtc);
                }

                if (incident.Status != IncidentStatus.Open || incident.AssignedToUserId is not null)
                {
                    Assert.NotNull(incident.AssignedToUserId);
                    Assert.NotNull(incident.SlaStartedAtUtc);
                    Assert.NotNull(incident.SlaTargetHours);
                }
            }

            // The SLA board shows a mix, not just green.
            var states = incidents.Select(i => SlaCalculator.Evaluate(i, now).State).ToHashSet();
            Assert.Contains(SlaState.OnTrack, states);
            Assert.Contains(SlaState.Breached, states);
            Assert.Contains(SlaState.Paused, states);
            Assert.Contains(SlaState.Met, states);
            Assert.Contains(SlaState.NotStarted, states);

            // The story has history and conversation to read.
            Assert.NotEmpty(await db.IncidentHistory.ToListAsync());
            Assert.NotEmpty(await db.IncidentComments.ToListAsync());

            // A second run changes nothing.
            Assert.False(await DemoDataSeeder.SeedAsync(db, userManager, ApiFactory.StandardPassword));
            Assert.Equal(14, await db.Incidents.CountAsync());
        }
    }
}
