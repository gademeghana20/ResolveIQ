
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using ResolveIQ.API.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using ResolveIQ.API.ErrorHandling;
using ResolveIQ.API.Health;
using ResolveIQ.API.Maintenance;
using ResolveIQ.API.Registration;
using ResolveIQ.API.Security;
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
    .AddEntityFrameworkStores<ApplicationDbContext>()
    // Password-reset links need these token providers.
    .AddDefaultTokenProviders();

// A password-reset link stops working after an hour (the default is a whole day).
builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));

builder.Services.Configure<AppSettings>(builder.Configuration.GetSection(AppSettings.SectionName));
builder.Services.Configure<DemoSettings>(builder.Configuration.GetSection(DemoSettings.SectionName));

// Behind a hosting service's proxy every request seems to come from the proxy's own address, which would make the
// per-address rate limits shared by the whole world. With Hosting:TrustProxyHeaders=true the real address (and
// http/https) is read from the proxy's X-Forwarded-* headers. Off by default: on a computer reachable directly,
// those headers could be forged.
var trustProxyHeaders = builder.Configuration.GetValue<bool>("Hosting:TrustProxyHeaders");
if (trustProxyHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}
// Development email: written to the log, not delivered. Swap this line for a real sender to send actual mail.
builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActor, CurrentActor>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<IAuditLog>(sp => sp.GetRequiredService<AuditLogService>());
builder.Services.AddScoped<IAuditService>(sp => sp.GetRequiredService<AuditLogService>());
builder.Services.AddScoped<INotifier, Notifier>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// Who may register, how fast one address may call the sign-in endpoints, and the background housekeeping.
builder.Services.Configure<RegistrationSettings>(builder.Configuration.GetSection(RegistrationSettings.SectionName));
builder.Services.Configure<MaintenanceSettings>(builder.Configuration.GetSection(MaintenanceSettings.SectionName));
builder.Services.AddAppRateLimiting(builder.Configuration);
builder.Services.AddScoped<MaintenanceJobs>();
builder.Services.AddHostedService<MaintenanceService>();

// "Is the app up, and can it reach its database?" for hosting services to ask.
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

// Do not announce which web server software this is.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

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
            .WithExposedHeaders("Content-Disposition", "X-Export-Truncated") // readable by the CSV download
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
    // Hosts without a command line cannot run the database tool, so the app can bring its own database up to date
    // when Database:MigrateOnStartup=true. Off by default, so a normal start never changes the database structure.
    if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    {
        var migrationDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await migrationDb.Database.MigrateAsync();
        app.Logger.LogInformation("The database structure is up to date.");
    }

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

    // Demo users exist in Development, or on a public demo site that asks for them with Seed:DemoUsers=true.
    // Either way the password comes from settings (user secrets or an environment variable), never from code.
    if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Seed:DemoUsers"))
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

            // Optional made-up incidents and people, only when asked for (Seed:DemoData=true) and only into an
            // empty database, so it can never add to real data.
            if (app.Configuration.GetValue<bool>("Seed:DemoData"))
            {
                var added = await DemoDataSeeder.SeedAsync(db, userManager, demoPassword);
                app.Logger.LogInformation(added
                    ? "Added the demo incidents and people."
                    : "Demo data was not added (the database already has incidents).");
            }
        }
    }
}

// Configure the HTTP request pipeline.

// Very first, so everything after it (rate limits, https) sees the visitor's real address and scheme.
if (trustProxyHeaders)
{
    app.UseForwardedHeaders();
}

// First of all, so even errors and refusals carry the browser-safety headers.
app.UseSecurityHeaders(allowInteractiveDocs: app.Environment.IsDevelopment());

// Next, so it can catch a crash from anything that runs after it.
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

// Tell browsers to use HTTPS only (not in development, where http://localhost is normal).
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
}

app.UseRateLimiter();

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

// Open to anyone and never rate limited, and it answers only Healthy or Unhealthy.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = (context, report) => context.Response.WriteAsJsonAsync(new { status = report.Status.ToString() })
}).DisableRateLimiting();

app.Run();

// Lets the automated tests start the real API in memory.
public partial class Program { }
