
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using ResolveIQ.API.Authentication;
using ResolveIQ.API.ErrorHandling;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Infrastructure.Authentication;
using ResolveIQ.Infrastructure.Data;
using ResolveIQ.Infrastructure.Services;

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

// Configure JWT authentication: how the API validates incoming tokens.
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured. Set it with dotnet user-secrets.");

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim names exactly as written in the token ("sub", "role") instead of renaming them.
        options.MapInboundClaims = false;

        // After the signature and expiry pass, also check the user is still active and unchanged.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = TokenUserValidator.ValidateAsync
        };

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            NameClaimType = "sub",
            RoleClaimType = "role",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddScoped<IIncidentService, IncidentService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISlaPolicyService, SlaPolicyService>();

// The clock the app reads "now" from. Tests can swap in a fake one.
builder.Services.AddSingleton(TimeProvider.System);

// CORS: lets the Angular app (a different origin) call this API from the browser.
// Only the exact origins listed in settings are allowed, never "any origin".
// In production set them with environment variables, e.g. Cors__AllowedOrigins__0=https://your-site.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    if (allowedOrigins.Length > 0)
    {
        options.AddDefaultPolicy(policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()      // Authorization (the token) and Content-Type
            .AllowAnyMethod());    // GET, POST, PUT
    }
});

// Clean, consistent error responses (no stack traces leaking to clients).
builder.Services.AddGlobalErrorHandling();

// Show enums as readable text ("High") instead of numbers (3) in API responses.
// The OpenAPI/Swagger document reads the first setting; the controllers read the second.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// OpenAPI document, with a JWT "Bearer" scheme so Swagger UI shows an Authorize button.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Paste the token returned by /api/auth/login."
            }
        };

        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
        });

        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Seed the Admin, Manager and Employee roles if they do not exist yet.
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    await IdentitySeeder.SeedRolesAsync(roleManager);

    // Reference data every environment needs (incident categories).
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await ReferenceDataSeeder.SeedCategoriesAsync(db);
    await ReferenceDataSeeder.SeedSlaPoliciesAsync(db);

    // The first Admin comes from settings (environment variables in production, never from code).
    // It is only created when no Admin exists yet.
    var adminEmail = app.Configuration["Seed:AdminEmail"];
    var adminPassword = app.Configuration["Seed:AdminPassword"];
    var bootstrapUserManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
    {
        var outcome = await IdentitySeeder.EnsureFirstAdminAsync(bootstrapUserManager, adminEmail, adminPassword);

        if (outcome == BootstrapAdminResult.Created)
        {
            app.Logger.LogInformation("Created the first Admin account ({Email}).", adminEmail);
        }
        else if (outcome == BootstrapAdminResult.EmailAlreadyRegistered)
        {
            app.Logger.LogWarning(
                "Seed:AdminEmail is already registered as a normal user and no Admin exists. " +
                "It was NOT promoted. Choose another email, or promote it from the database.");
        }
    }
    else if (!string.IsNullOrWhiteSpace(adminEmail) || !string.IsNullOrWhiteSpace(adminPassword))
    {
        app.Logger.LogWarning("Set both Seed:AdminEmail and Seed:AdminPassword to create the first Admin.");
    }
    else if ((await bootstrapUserManager.GetUsersInRoleAsync(RoleNames.Admin)).Count == 0
             && !app.Environment.IsDevelopment())
    {
        app.Logger.LogWarning(
            "There is no Admin account. Set Seed:AdminEmail and Seed:AdminPassword to create the first one.");
    }

    // Demo users exist only in Development, with a password from user secrets.
    if (app.Environment.IsDevelopment())
    {
        var demoPassword = app.Configuration["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(demoPassword))
        {
            app.Logger.LogWarning("Seed:DemoPassword is not set; demo users were not created.");
        }
        else
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            await IdentitySeeder.SeedDemoUsersAsync(userManager, demoPassword);
        }
    }
}

// Configure the HTTP request pipeline.

// First, so it can catch a crash from anything that runs after it.
app.UseGlobalErrorHandling();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Swagger UI at /swagger, reading the built-in OpenAPI document.
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "ResolveIQ API v1");
        options.RoutePrefix = "swagger";
    });
}

// Before the HTTPS redirect: the browser's "preflight" check must be answered directly, not redirected.
// Before authentication: even a 401 reply must carry the CORS headers so Angular can read it.
app.UseCors();

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Lets the automated tests start the real API in memory.
public partial class Program { }
