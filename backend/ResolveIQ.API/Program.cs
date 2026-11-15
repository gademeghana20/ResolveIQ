
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ResolveIQ.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Register ApplicationDbContext with SQL Server.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// Register ASP.NET Core Identity (users, roles, password hashing) using our DbContext.
builder.Services
    .AddIdentityCore<IdentityUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddControllers();

builder.Services.AddOpenApi();

var app = builder.Build();

// Seed the Admin, Manager and Employee roles if they do not exist yet.
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    await IdentitySeeder.SeedRolesAsync(roleManager);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
