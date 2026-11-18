using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;

namespace ResolveIQ.Infrastructure.Data
{
    /// <summary>
    /// Fills an empty Development database with made-up people, incidents and comments, so a fresh clone of the
    /// project shows a working app straight away. Everything here is invented (example.com addresses, fictional
    /// problems). It only runs in Development, and only while there are no incidents yet, so it never touches real data.
    /// </summary>
    public static class DemoDataSeeder
    {
        private const string Admin = "admin@example.com";
        private const string Manager = "manager@example.com";
        private const string Employee = "employee@example.com";
        private const string Dana = "dana.lee@example.com";
        private const string Sam = "sam.ortiz@example.com";
        private const string Priya = "priya.nair@example.com";
        private const string Alex = "alex.kim@example.com";

        private static readonly (string Email, string Role)[] ExtraPeople =
        {
            (Alex, RoleNames.Manager),
            (Dana, RoleNames.Employee),
            (Sam, RoleNames.Employee),
            (Priya, RoleNames.Employee)
        };

        // The same hours the starting SLA policies promise.
        private static int HoursFor(IncidentPriority priority) => priority switch
        {
            IncidentPriority.Critical => 4,
            IncidentPriority.High => 8,
            IncidentPriority.Medium => 24,
            _ => 48
        };

        /// <summary>Returns true when demo data was added.</summary>
        public static async Task<bool> SeedAsync(
            ApplicationDbContext db, UserManager<IdentityUser> userManager, string password)
        {
            if (await db.Incidents.AnyAsync())
            {
                return false;
            }

            foreach (var (email, role) in ExtraPeople)
            {
                if (await userManager.FindByEmailAsync(email) is not null)
                {
                    continue;
                }

                var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
                var result = await userManager.CreateAsync(user, password);
                if (!result.Succeeded)
                {
                    var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"Could not seed demo user {email}: {errors}");
                }

                await userManager.AddToRoleAsync(user, role);
            }

            var ids = new Dictionary<string, string>();
            foreach (var email in new[] { Admin, Manager, Employee, Dana, Sam, Priya, Alex })
            {
                var user = await userManager.FindByEmailAsync(email);
                if (user is null)
                {
                    return false; // the basic demo users are missing; do nothing rather than half-fill
                }

                ids[email] = user.Id;
            }

            var categories = await db.Categories.ToDictionaryAsync(c => c.Name, c => c.Id);
            var builder = new Builder(db, ids, categories, DateTime.UtcNow);

            // ---- Waiting to be picked up (not assigned yet)
            await builder.AddAsync("Cannot connect to the office Wi-Fi",
                "Since this morning my laptop says 'No internet' on the office Wi-Fi. My phone works fine.",
                "Network", IncidentPriority.High, Dana, createdHoursAgo: 3);
            await builder.AddAsync("Request: access to the shared finance folder",
                "I moved to the finance team this week and need read access to the shared finance folder.",
                "Access and Permissions", IncidentPriority.Low, Sam, createdHoursAgo: 20);
            await builder.AddAsync("Monitor flickers every few minutes",
                "The left monitor on my desk goes black for a second, several times an hour.",
                "Hardware", null, Priya, createdHoursAgo: 30);

            // ---- Assigned, work not started
            await builder.AddAsync("Excel crashes when opening the monthly report",
                "The monthly sales report closes Excel without any message as soon as I open it.",
                "Software", IncidentPriority.Medium, Employee, createdHoursAgo: 5,
                assignTo: Manager, priority: IncidentPriority.Medium, assignedHoursAgo: 4);

            // ---- In progress: one on track, one at risk, one overdue
            await builder.AddAsync("VPN drops every 10 minutes when working from home",
                "The VPN disconnects about every ten minutes and I have to sign in again.",
                "Network", IncidentPriority.High, Dana, createdHoursAgo: 4,
                assignTo: Admin, priority: IncidentPriority.High, assignedHoursAgo: 3, startedHoursAgo: 2,
                comments: new[]
                {
                    (Admin, 2.0, "Thanks Dana, I can see your sessions dropping in the VPN logs. Looking at the timeout setting now."),
                    (Dana, 1.5, "It happened again just now at 10:42, in case that helps.")
                });
            await builder.AddAsync("Printer on floor 2 keeps jamming",
                "Paper jams every time we print double-sided. Single-sided is fine.",
                "Hardware", IncidentPriority.Medium, Sam, createdHoursAgo: 22,
                assignTo: Alex, priority: IncidentPriority.Medium, assignedHoursAgo: 19, startedHoursAgo: 18,
                comments: new[]
                {
                    (Alex, 18.0, "I will check the paper tray roller on my next visit to floor 2.")
                });
            await builder.AddAsync("Customer database is very slow in the afternoon",
                "Searching customers takes more than 30 seconds between 2 and 4 pm. Mornings are fine.",
                "Database", IncidentPriority.Critical, Priya, createdHoursAgo: 40,
                assignTo: Admin, priority: IncidentPriority.Critical, assignedHoursAgo: 36, startedHoursAgo: 35,
                comments: new[]
                {
                    (Admin, 34.0, "Found a report job that runs at 2 pm and locks a large table. Testing a lighter schedule."),
                    (Manager, 30.0, "Please keep me posted. Customer support is complaining.")
                });

            // ---- On hold (waiting for something)
            await builder.AddAsync("New starter needs a laptop and accounts",
                "A new colleague starts on Monday and needs a laptop, an email account and access to the team folder.",
                "Access and Permissions", IncidentPriority.Medium, Manager, createdHoursAgo: 22,
                assignTo: Alex, priority: IncidentPriority.Medium, assignedHoursAgo: 20, startedHoursAgo: 19,
                holdHoursAgo: 10, holdReason: "Waiting for the laptop to arrive from the supplier.");

            // ---- Resolved, waiting for the reporter to confirm
            await builder.AddAsync("Software licence expired for the design tool",
                "The design tool says the licence has expired and will not open any files.",
                "Software", IncidentPriority.High, Priya, createdHoursAgo: 28,
                assignTo: Manager, priority: IncidentPriority.High, assignedHoursAgo: 26, startedHoursAgo: 25,
                resolvedHoursAgo: 20, resolutionNotes: "Renewed the licence and re-activated it on your laptop. Please restart the tool once.",
                comments: new[]
                {
                    (Manager, 25.0, "Procurement confirmed the renewal. Applying it now.")
                });
            await builder.AddAsync("Forgot my password and the reset email never arrives",
                "I asked for a password reset three times and nothing arrived.",
                "Access and Permissions", IncidentPriority.Medium, Employee, createdHoursAgo: 8,
                assignTo: Admin, priority: IncidentPriority.Medium, assignedHoursAgo: 7, startedHoursAgo: 7,
                resolvedHoursAgo: 6, resolutionNotes: "The reset emails were going to your spam folder. I set a temporary password and you can change it after signing in.");

            // ---- Closed
            await builder.AddAsync("Keyboard keys stuck on my laptop",
                "The E and R keys stick and sometimes type twice.",
                "Hardware", IncidentPriority.Low, Sam, createdHoursAgo: 24 * 6,
                assignTo: Alex, priority: IncidentPriority.Low, assignedHoursAgo: 24 * 6 - 3, startedHoursAgo: 24 * 6 - 4,
                resolvedHoursAgo: 24 * 5, resolutionNotes: "Swapped in a replacement keyboard.",
                closedHoursAgo: 24 * 4);
            await builder.AddAsync("Cannot open the shared project spreadsheet",
                "I get 'You do not have permission' on the project spreadsheet.",
                "Access and Permissions", IncidentPriority.Medium, Dana, createdHoursAgo: 24 * 9,
                assignTo: Admin, priority: IncidentPriority.Medium, assignedHoursAgo: 24 * 9 - 2, startedHoursAgo: 24 * 9 - 3,
                resolvedHoursAgo: 24 * 8, resolutionNotes: "Added you to the Project team group, which owns the file.",
                closedHoursAgo: 24 * 7,
                comments: new[]
                {
                    (Admin, 24.0 * 8, "Added you to the group. Try again in a few minutes while the change spreads."),
                    (Dana, 24.0 * 7.5, "Works now, thank you!")
                });
            await builder.AddAsync("Email app shows an old signature",
                "My emails still go out with the signature from my old role.",
                "Software", IncidentPriority.Low, Priya, createdHoursAgo: 24 * 14,
                assignTo: Manager, priority: IncidentPriority.Low, assignedHoursAgo: 24 * 14 - 5, startedHoursAgo: 24 * 14 - 6,
                resolvedHoursAgo: 24 * 12, resolutionNotes: "Updated the signature in the account settings.",
                closedHoursAgo: 24 * 11);
            await builder.AddAsync("Meeting room display does not turn on",
                "The screen in meeting room B stays black. The power light is on.",
                "Hardware", IncidentPriority.Medium, Employee, createdHoursAgo: 24 * 19,
                assignTo: Alex, priority: IncidentPriority.Medium, assignedHoursAgo: 24 * 19 - 2, startedHoursAgo: 24 * 19 - 3,
                resolvedHoursAgo: 24 * 18, resolutionNotes: "The display was set to the wrong input. Switched it back to HDMI 1.",
                closedHoursAgo: 24 * 17);

            return true;
        }

        /// <summary>Builds one incident with the history rows and comments the real app would have written.</summary>
        private sealed class Builder
        {
            private readonly ApplicationDbContext _db;
            private readonly Dictionary<string, string> _ids;
            private readonly Dictionary<string, Guid> _categories;
            private readonly DateTime _now;

            public Builder(ApplicationDbContext db, Dictionary<string, string> ids, Dictionary<string, Guid> categories, DateTime now)
            {
                _db = db;
                _ids = ids;
                _categories = categories;
                _now = now;
            }

            public async Task AddAsync(
                string title,
                string description,
                string category,
                IncidentPriority? urgency,
                string reporter,
                double createdHoursAgo,
                string? assignTo = null,
                IncidentPriority? priority = null,
                double assignedHoursAgo = 0,
                double? startedHoursAgo = null,
                double? holdHoursAgo = null,
                string? holdReason = null,
                double? resolvedHoursAgo = null,
                string? resolutionNotes = null,
                double? closedHoursAgo = null,
                (string Author, double HoursAgo, string Text)[]? comments = null)
            {
                var numbers = await _db.Database
                    .SqlQueryRaw<long>("SELECT NEXT VALUE FOR " + ApplicationDbContext.IncidentNumberSequence + " AS [Value]")
                    .ToListAsync();

                DateTime At(double hoursAgo) => _now.AddHours(-hoursAgo);

                var incident = new Incident
                {
                    IncidentNumber = $"INC-{numbers[0]:D6}",
                    Title = title,
                    Description = description,
                    CategoryId = _categories.TryGetValue(category, out var categoryId) ? categoryId : null,
                    ReportedUrgency = urgency,
                    ReportedByUserId = _ids[reporter],
                    Status = IncidentStatus.Open,
                    CreatedAtUtc = At(createdHoursAgo)
                };
                _db.Incidents.Add(incident);

                if (assignTo is not null)
                {
                    var assignedAt = At(assignedHoursAgo);
                    incident.AssignedToUserId = _ids[assignTo];
                    incident.Priority = priority;
                    incident.SlaStartedAtUtc = assignedAt;
                    incident.SlaTargetHours = priority is { } p ? HoursFor(p) : null;
                    incident.UpdatedAtUtc = assignedAt;

                    History(incident, Manager, "AssignedTo", null, assignTo, assignedAt);
                    if (priority is { } pr)
                    {
                        History(incident, Manager, "Priority", null, pr.ToString(), assignedAt);
                    }

                    History(incident, Manager, "SlaClock", null, "Started", assignedAt);
                }

                if (startedHoursAgo is double started)
                {
                    var at = At(started);
                    incident.Status = IncidentStatus.InProgress;
                    incident.UpdatedAtUtc = at;
                    History(incident, assignTo!, "Status", "Open", "InProgress", at);
                }

                if (holdHoursAgo is double hold)
                {
                    var at = At(hold);
                    incident.Status = IncidentStatus.OnHold;
                    incident.SlaPausedAtUtc = at;
                    incident.UpdatedAtUtc = at;
                    History(incident, assignTo!, "Status", "InProgress", "OnHold", at);
                    History(incident, assignTo!, "OnHoldReason", null, holdReason, at);
                }

                if (resolvedHoursAgo is double resolved)
                {
                    var at = At(resolved);
                    incident.Status = IncidentStatus.Resolved;
                    incident.ResolvedAtUtc = at;
                    incident.ResolutionNotes = resolutionNotes;
                    incident.UpdatedAtUtc = at;
                    History(incident, assignTo!, "Status", "InProgress", "Resolved", at);
                    History(incident, assignTo!, "ResolutionNotes", null, resolutionNotes, at);
                }

                if (closedHoursAgo is double closed)
                {
                    var at = At(closed);
                    incident.Status = IncidentStatus.Closed;
                    incident.ClosedAtUtc = at;
                    incident.UpdatedAtUtc = at;
                    History(incident, reporter, "Status", "Resolved", "Closed", at);
                }

                foreach (var (author, hoursAgo, text) in comments ?? Array.Empty<(string, double, string)>())
                {
                    _db.IncidentComments.Add(new IncidentComment
                    {
                        IncidentId = incident.Id,
                        AuthorUserId = _ids[author],
                        Text = text,
                        CreatedAtUtc = At(hoursAgo)
                    });
                }

                // Save one incident at a time so the counters (Sequence) follow the story's order.
                await _db.SaveChangesAsync();
            }

            private void History(Incident incident, string by, string field, string? oldValue, string? newValue, DateTime at)
            {
                _db.IncidentHistory.Add(new IncidentHistory
                {
                    IncidentId = incident.Id,
                    ChangedByUserId = _ids[by],
                    FieldChanged = field,
                    OldValue = oldValue,
                    NewValue = newValue,
                    ChangedAtUtc = at
                });
            }
        }
    }
}
