using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Krylya.Api;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Krylya")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Krylya.");

builder.Services.AddDbContext<KrylyaDb>(options => options.UseNpgsql(connection));
builder.Services.AddSingleton<IPasswordHasher<Account>, PasswordHasher<Account>>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<MonthlyCompetition>();
builder.Services.AddHostedService<MonthlyCompetitionWorker>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-Token";
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "Krylya.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
    options.Events.OnValidatePrincipal = async context =>
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<KrylyaDb>();
        if (!Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            || !int.TryParse(context.Principal.FindFirstValue(AuthEndpoints.SessionVersionClaim) ?? "0", out var version)
            || !await db.Accounts.AnyAsync(a => a.Id == id && a.Active && a.SessionVersion == version,
                context.HttpContext.RequestAborted))
            context.RejectPrincipal();
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole(Roles.Admin));
    options.AddPolicy("Staff", policy => policy.RequireRole(Roles.Coach, Roles.Admin));
    options.AddPolicy("Pilot", policy => policy.RequireRole(Roles.Pilot));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("Login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
if (args.Contains("--migrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<KrylyaDb>().Database.MigrateAsync();
    return;
}
if (args.Contains("--bootstrap-coach") || args.Contains("--bootstrap-admin"))
{
    var role = args.Contains("--bootstrap-admin") ? Roles.Admin : Roles.Coach;
    await AuthEndpoints.BootstrapAsync(app.Services, app.Configuration, role);
    return;
}

if (args.Contains("--disable-staff") || args.Contains("--recover-staff"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<KrylyaDb>();
    var login = builder.Configuration["Bootstrap:Login"]?.Trim().ToLowerInvariant();
    var staff = db.Accounts.Where(a => a.Login == login && (a.Role == Roles.Admin || a.Role == Roles.Coach));
    var account = await staff.SingleOrDefaultAsync() ?? throw new InvalidOperationException("Staff login not found.");
    if (args.Contains("--disable-staff"))
        await staff.ExecuteUpdateAsync(set => set.SetProperty(a => a.Active, false)
            .SetProperty(a => a.SessionVersion, a => a.SessionVersion + 1));
    else
    {
        var password = builder.Configuration["Bootstrap:Password"] ?? "";
        if (password.Length is < 12 or > 128) throw new InvalidOperationException("Provide a new 12–128 character password.");
        var hash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>().HashPassword(account, password);
        await staff.ExecuteUpdateAsync(set => set.SetProperty(a => a.PasswordHash, hash).SetProperty(a => a.Active, true)
            .SetProperty(a => a.SessionVersion, a => a.SessionVersion + 1));
    }
    return;
}

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
var configuredFrontend = app.Configuration["Frontend:Directory"];
string? frontendIndex = null;
if (!string.IsNullOrWhiteSpace(configuredFrontend))
{
    var directory = Path.GetFullPath(configuredFrontend, app.Environment.ContentRootPath);
    frontendIndex = Path.Combine(directory, "index.html");
    if (!File.Exists(frontendIndex))
        throw new InvalidOperationException("Frontend:Directory must contain the built Vue index.html.");
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(directory) });
}
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api")
        && (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method)
            || HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))
        && !await context.RequestServices.GetRequiredService<IAntiforgery>().IsRequestValidAsync(context))
    {
        await Results.Problem("Получите токен сессии и повторите запрос.", statusCode: 400).ExecuteAsync(context);
        return;
    }
    await next(context);
});

if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.MapGet("/health", async (KrylyaDb db, CancellationToken ct) =>
{
    var expectedMonth = CompetitionEndpoints.MonthAt(DateTimeOffset.UtcNow.AddMinutes(-2));
    return await db.Database.CanConnectAsync(ct) && !(await db.Database.GetPendingMigrationsAsync(ct)).Any()
        && !await db.Challenges.AnyAsync(c => c.FinalizedAt == null &&
            c.Month < expectedMonth, ct)
        ? Results.Ok() : Results.StatusCode(503);
});
app.MapHistory();
app.MapAuth();
app.MapTraining();
app.MapCompetition();
if (frontendIndex is not null)
{
    var index = frontendIndex;
    app.MapFallback((HttpContext context) => context.Request.Path.StartsWithSegments("/api")
        ? Results.NotFound()
        : Results.File(index, "text/html; charset=utf-8"));
}
app.Run();
