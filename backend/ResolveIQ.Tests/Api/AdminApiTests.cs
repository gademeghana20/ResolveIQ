using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResolveIQ.Domain.Constants;

namespace ResolveIQ.Tests.Api
{
    [Collection(ApiCollection.Name)]
    public class UserAdminTests
    {
        private readonly ApiFactory _factory;

        public UserAdminTests(ApiFactory factory) => _factory = factory;

        private static string NewEmail() => $"created.{Guid.NewGuid():N}@example.com";

        private static IEnumerable<string?> Roles(JsonElement user) =>
            user.GetProperty("roles").EnumerateArray().Select(r => r.GetString());

        // ---- looking users up ----

        [Fact]
        public async Task ManagersAndAdminsCanListAndReadUsers_EmployeesCannot()
        {
            var employee = await _factory.CreateUserAsync();
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);

            await employee.Client.GetAsync("/api/users").ExpectAsync(HttpStatusCode.Forbidden);
            await employee.Client.GetAsync($"/api/users/{manager.Id}").ExpectAsync(HttpStatusCode.Forbidden);

            foreach (var viewer in new[] { manager, admin })
            {
                var list = await (await viewer.Client.GetAsync("/api/users").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();
                var me = list.EnumerateArray().Single(u => u.Str("email") == employee.Email);

                Assert.Equal(new[] { RoleNames.Employee }, Roles(me));
                Assert.True(me.GetProperty("isActive").GetBoolean());

                var one = await (await viewer.Client.GetAsync($"/api/users/{employee.Id}").ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();
                Assert.Equal(employee.Email, one.Str("email"));
            }
        }

        [Fact]
        public async Task AnUnknownUserIsNotFound()
        {
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);

            await manager.Client.GetAsync("/api/users/no-such-user").ExpectAsync(HttpStatusCode.NotFound);
        }

        // ---- creating users ----

        [Theory]
        [InlineData("manager", "Manager")]
        [InlineData("ADMIN", "Admin")]
        [InlineData("Employee", "Employee")]
        public async Task AnAdminCanCreateAUserWithAnyRole_TypedInAnyCapitals(string typedRole, string expectedRole)
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var email = NewEmail();

            var response = await admin.Client.PostAsJsonAsync("/api/users",
                new { email, password = ApiFactory.StandardPassword, role = typedRole }).ExpectAsync(HttpStatusCode.Created);
            var created = await response.ReadJsonAsync();

            Assert.Equal(new[] { expectedRole }, Roles(created));
            Assert.True(created.GetProperty("isActive").GetBoolean());
            Assert.NotNull(response.Headers.Location);

            // And that person can log in with the role they were given.
            var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
                new { email, password = ApiFactory.StandardPassword }).ExpectAsync(HttpStatusCode.OK);
            Assert.Equal(new[] { expectedRole }, Roles(await login.ReadJsonAsync()));
        }

        [Fact]
        public async Task CreatingAUserRejectsBadInput()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var taken = await _factory.CreateUserAsync();

            var badBodies = new object[]
            {
                new { email = NewEmail(), password = ApiFactory.StandardPassword, role = "Boss" },
                new { email = NewEmail(), password = ApiFactory.StandardPassword, role = "" },
                new { email = NewEmail(), password = "abc", role = "Employee" },
                new { email = "not-an-email", password = ApiFactory.StandardPassword, role = "Employee" },
                new { email = taken.Email, password = ApiFactory.StandardPassword, role = "Employee" },
                new { email = taken.Email.ToUpperInvariant(), password = ApiFactory.StandardPassword, role = "Employee" }
            };

            foreach (var body in badBodies)
            {
                await admin.Client.PostAsJsonAsync("/api/users", body).ExpectAsync(HttpStatusCode.BadRequest);
            }
        }

        [Fact]
        public async Task ANewUserWithAWeakPassword_IsToldWhichRulesItBroke()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);

            var response = await admin.Client.PostAsJsonAsync("/api/users",
                new { email = NewEmail(), password = "abc", role = "Employee" }).ExpectAsync(HttpStatusCode.BadRequest);

            var errors = (await response.ReadJsonAsync()).GetProperty("errors");
            Assert.True(errors.GetProperty("Password").GetArrayLength() >= 1);
        }

        [Fact]
        public async Task ManagersAndEmployeesCannotCreateUsers()
        {
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var employee = await _factory.CreateUserAsync();
            var body = new { email = NewEmail(), password = ApiFactory.StandardPassword, role = "Employee" };

            await manager.Client.PostAsJsonAsync("/api/users", body).ExpectAsync(HttpStatusCode.Forbidden);
            await employee.Client.PostAsJsonAsync("/api/users", body).ExpectAsync(HttpStatusCode.Forbidden);
        }

        // ---- changing roles ----

        [Fact]
        public async Task ChangingARole_TakesEffectAtOnce_TheOldTokenStopsWorking()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync(RoleNames.Manager);

            await person.Client.GetAsync("/api/users").ExpectAsync(HttpStatusCode.OK);         // a Manager can

            var response = await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/role", new { role = "Employee" })
                .ExpectAsync(HttpStatusCode.OK);
            Assert.Equal(new[] { RoleNames.Employee }, Roles(await response.ReadJsonAsync()));

            // The token they logged in with earlier is dead, immediately, not in an hour.
            await person.Client.GetAsync("/api/users").ExpectAsync(HttpStatusCode.Unauthorized);

            // A fresh login carries the new role.
            await person.LoginAsync();
            await person.Client.GetAsync("/api/users").ExpectAsync(HttpStatusCode.Forbidden);   // an Employee cannot
            await person.Client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task PromotingSomeone_GivesThemTheirNewPowersOnTheNextLogin()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();

            await person.Client.GetAsync("/api/users").ExpectAsync(HttpStatusCode.Forbidden);
            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/role", new { role = "manager" }).ExpectAsync(HttpStatusCode.OK);

            await person.LoginAsync();
            await person.Client.GetAsync("/api/users").ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task SettingTheSameRoleAgainIsHarmless_AndLeavesTheTokenValid()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync(RoleNames.Manager);

            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/role", new { role = "Manager" }).ExpectAsync(HttpStatusCode.OK);

            await person.Client.GetAsync("/api/users").ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ChangingARoleHasRules()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var person = await _factory.CreateUserAsync();

            // Nobody changes their own role, so there is always an Admin left to fix mistakes.
            await admin.Client.PutAsJsonAsync($"/api/users/{admin.Id}/role", new { role = "Employee" }).ExpectAsync(HttpStatusCode.BadRequest);

            await manager.Client.PutAsJsonAsync($"/api/users/{person.Id}/role", new { role = "Admin" }).ExpectAsync(HttpStatusCode.Forbidden);
            await person.Client.PutAsJsonAsync($"/api/users/{person.Id}/role", new { role = "Admin" }).ExpectAsync(HttpStatusCode.Forbidden);

            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/role", new { role = "Boss" }).ExpectAsync(HttpStatusCode.BadRequest);
            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/role", new { role = "" }).ExpectAsync(HttpStatusCode.BadRequest);
            await admin.Client.PutAsJsonAsync("/api/users/no-such-user/role", new { role = "Manager" }).ExpectAsync(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task ChangingOnePersonsRole_DoesNotLogAnyoneElseOut()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var bystander = await _factory.CreateUserAsync(RoleNames.Manager);
            var person = await _factory.CreateUserAsync();

            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/role", new { role = "Manager" }).ExpectAsync(HttpStatusCode.OK);

            await bystander.Client.GetAsync("/api/users").ExpectAsync(HttpStatusCode.OK);
            await admin.Client.GetAsync("/api/users").ExpectAsync(HttpStatusCode.OK);
        }

        // ---- deactivating ----

        [Fact]
        public async Task Deactivating_LocksTheUserOutAtOnce_AndReactivatingLetsThemBackIn()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();
            await person.Client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.OK);

            var response = await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/active", new { isActive = false })
                .ExpectAsync(HttpStatusCode.OK);
            Assert.False((await response.ReadJsonAsync()).GetProperty("isActive").GetBoolean());

            // The token they already hold stops working at once, and they cannot log in again.
            await person.Client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.Unauthorized);
            await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = person.Email, password = person.Password })
                .ExpectAsync(HttpStatusCode.Unauthorized);

            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/active", new { isActive = true }).ExpectAsync(HttpStatusCode.OK);

            await person.LoginAsync();
            await person.Client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task DeactivationHasRules()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var person = await _factory.CreateUserAsync();

            await admin.Client.PutAsJsonAsync($"/api/users/{admin.Id}/active", new { isActive = false }).ExpectAsync(HttpStatusCode.BadRequest);
            await manager.Client.PutAsJsonAsync($"/api/users/{person.Id}/active", new { isActive = false }).ExpectAsync(HttpStatusCode.Forbidden);
            await person.Client.PutAsJsonAsync($"/api/users/{person.Id}/active", new { isActive = false }).ExpectAsync(HttpStatusCode.Forbidden);
            await admin.Client.PutAsJsonAsync("/api/users/no-such-user/active", new { isActive = false }).ExpectAsync(HttpStatusCode.NotFound);

            // Through all of that, the person was never actually deactivated.
            await person.Client.GetAsync("/api/incidents").ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task AnInactiveUserCannotBeAssignedIncidents_UntilReactivated()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var s = await Scenario.UnassignedAsync(_factory);
            await admin.Client.PutAsJsonAsync($"/api/users/{s.Assignee.Id}/active", new { isActive = false }).ExpectAsync(HttpStatusCode.OK);

            var response = await s.Manager.Client.PutAsJsonAsync($"{s.Url}/assign",
                new { assigneeUserId = s.Assignee.Id, priority = "High" }).ExpectAsync(HttpStatusCode.BadRequest);
            Assert.True((await response.ReadJsonAsync()).GetProperty("errors").TryGetProperty("AssigneeUserId", out _));

            await admin.Client.PutAsJsonAsync($"/api/users/{s.Assignee.Id}/active", new { isActive = true }).ExpectAsync(HttpStatusCode.OK);
            await s.Manager.Client.PutAsJsonAsync($"{s.Url}/assign",
                new { assigneeUserId = s.Assignee.Id, priority = "High" }).ExpectAsync(HttpStatusCode.OK);
        }

        [Fact]
        public async Task TheUserListShowsWhoIsInactive()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var person = await _factory.CreateUserAsync();
            await admin.Client.PutAsJsonAsync($"/api/users/{person.Id}/active", new { isActive = false }).ExpectAsync(HttpStatusCode.OK);

            var list = await (await admin.Client.GetAsync("/api/users")).ReadJsonAsync();
            var row = list.EnumerateArray().Single(u => u.Str("email") == person.Email);

            Assert.False(row.GetProperty("isActive").GetBoolean());
        }
    }

    [Collection(ApiCollection.Name)]
    public class CategoryTests
    {
        private readonly ApiFactory _factory;

        public CategoryTests(ApiFactory factory) => _factory = factory;

        private static string NewName() => $"Category {Guid.NewGuid():N}"[..30];

        private static async Task<List<JsonElement>> ListAsync(TestUser who, string query = "")
        {
            var response = await who.Client.GetAsync("/api/categories" + query).ExpectAsync(HttpStatusCode.OK);
            return (await response.ReadJsonAsync()).EnumerateArray().ToList();
        }

        [Fact]
        public async Task EveryoneLoggedInCanListTheSixStartingCategories_InNameOrder()
        {
            var employee = await _factory.CreateUserAsync();

            var names = (await ListAsync(employee)).Select(c => c.Str("name")).ToList();

            Assert.Contains("Hardware", names);
            Assert.Contains("Software", names);
            Assert.Contains("Network", names);
            Assert.Contains("Access and Permissions", names);
            Assert.Contains("Database", names);
            Assert.Contains("Other", names);
            Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names);
        }

        [Fact]
        public async Task AnAdminCreatesACategory_WhichEveryoneThenSees()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var employee = await _factory.CreateUserAsync();
            var name = NewName();

            var response = await admin.Client.PostAsJsonAsync("/api/categories",
                new { name = $"  {name}  ", description = "  For tests  " }).ExpectAsync(HttpStatusCode.Created);
            var created = await response.ReadJsonAsync();

            Assert.Equal(name, created.Str("name"));                   // trimmed
            Assert.Equal("For tests", created.Str("description"));
            Assert.True(created.GetProperty("isActive").GetBoolean());
            Assert.Contains(await ListAsync(employee), c => c.Str("name") == name);
        }

        [Fact]
        public async Task OnlyAdminsCanCreateOrEditCategories()
        {
            var employee = await _factory.CreateUserAsync();
            var manager = await _factory.CreateUserAsync(RoleNames.Manager);
            var anyCategory = (await ListAsync(employee)).First().GetGuid("id");

            foreach (var person in new[] { employee, manager })
            {
                await person.Client.PostAsJsonAsync("/api/categories", new { name = NewName() }).ExpectAsync(HttpStatusCode.Forbidden);
                await person.Client.PutAsJsonAsync($"/api/categories/{anyCategory}", new { name = NewName(), isActive = true })
                    .ExpectAsync(HttpStatusCode.Forbidden);
            }
        }

        [Fact]
        public async Task CategoryNamesMustBeUnique_IgnoringCapitals_AndNotEmpty()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var name = NewName();
            await admin.Client.PostAsJsonAsync("/api/categories", new { name }).ExpectAsync(HttpStatusCode.Created);

            await admin.Client.PostAsJsonAsync("/api/categories", new { name }).ExpectAsync(HttpStatusCode.BadRequest);
            await admin.Client.PostAsJsonAsync("/api/categories", new { name = name.ToUpperInvariant() }).ExpectAsync(HttpStatusCode.BadRequest);
            await admin.Client.PostAsJsonAsync("/api/categories", new { name = "hardware" }).ExpectAsync(HttpStatusCode.BadRequest);
            await admin.Client.PostAsJsonAsync("/api/categories", new { name = "" }).ExpectAsync(HttpStatusCode.BadRequest);
            await admin.Client.PostAsJsonAsync("/api/categories", new { name = "   " }).ExpectAsync(HttpStatusCode.BadRequest);
            await admin.Client.PostAsJsonAsync("/api/categories", new { name = new string('x', 101) }).ExpectAsync(HttpStatusCode.BadRequest);
            await admin.Client.PostAsJsonAsync("/api/categories", new { name = NewName(), description = new string('x', 501) }).ExpectAsync(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task RenamingWorks_ButNotToANameAlreadyInUse()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var first = await (await admin.Client.PostAsJsonAsync("/api/categories", new { name = NewName() }).ExpectAsync(HttpStatusCode.Created)).ReadJsonAsync();
            var second = await (await admin.Client.PostAsJsonAsync("/api/categories", new { name = NewName() }).ExpectAsync(HttpStatusCode.Created)).ReadJsonAsync();
            var url = $"/api/categories/{first.GetGuid("id")}";

            await admin.Client.PutAsJsonAsync(url, new { name = second.Str("name"), isActive = true }).ExpectAsync(HttpStatusCode.BadRequest);
            await admin.Client.PutAsJsonAsync(url, new { name = first.Str("name"), description = "same name is fine", isActive = true })
                .ExpectAsync(HttpStatusCode.OK);

            var newName = NewName();
            var renamed = await (await admin.Client.PutAsJsonAsync(url, new { name = newName, isActive = true }).ExpectAsync(HttpStatusCode.OK)).ReadJsonAsync();
            Assert.Equal(newName, renamed.Str("name"));

            await admin.Client.PutAsJsonAsync($"/api/categories/{Guid.NewGuid()}", new { name = NewName(), isActive = true })
                .ExpectAsync(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task ADeactivatedCategory_DisappearsFromTheList_AndCannotBeUsedForNewIncidents()
        {
            var admin = await _factory.CreateUserAsync(RoleNames.Admin);
            var employee = await _factory.CreateUserAsync();
            var name = NewName();
            var created = await (await admin.Client.PostAsJsonAsync("/api/categories", new { name }).ExpectAsync(HttpStatusCode.Created)).ReadJsonAsync();
            var id = created.GetGuid("id");

            // An incident can use it while it is active...
            var before = await employee.ReportIncidentAsync("uses the category", categoryId: id);

            await admin.Client.PutAsJsonAsync($"/api/categories/{id}", new { name, isActive = false }).ExpectAsync(HttpStatusCode.OK);

            // ...it vanishes from everyone's list, and admins can still ask for it...
            Assert.DoesNotContain(await ListAsync(employee), c => c.Str("name") == name);
            Assert.DoesNotContain(await ListAsync(employee, "?includeInactive=true"), c => c.Str("name") == name);   // employees cannot ask
            Assert.Contains(await ListAsync(admin, "?includeInactive=true"), c => c.Str("name") == name);

            // ...new incidents cannot use it...
            await employee.Client.PostAsJsonAsync("/api/incidents", new { title = "t", description = "d", categoryId = id })
                .ExpectAsync(HttpStatusCode.BadRequest);

            // ...but the old incident keeps its category.
            Assert.Equal(name, (await employee.GetIncidentAsync(before)).Str("categoryName"));
        }

        [Fact]
        public async Task TheCategoryListNeedsALogin()
        {
            await _factory.CreateClient().GetAsync("/api/categories").ExpectAsync(HttpStatusCode.Unauthorized);
        }
    }
}
