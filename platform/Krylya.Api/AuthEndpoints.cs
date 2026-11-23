using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Krylya.Api;

internal static class AuthEndpoints
{
    internal const string SessionVersionClaim = "krylya_session_version";
    public static void MapAuth(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");
        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var token = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = token.RequestToken });
        });
        auth.MapPost("/login", LoginAsync).RequireRateLimiting("Login");
        auth.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
        auth.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            id = Actor(user), login = user.Identity!.Name, role = user.FindFirstValue(ClaimTypes.Role)
        })).RequireAuthorization();

        var staff = app.MapGroup("/api/staff").RequireAuthorization("Staff");
        var admin = app.MapGroup("/api/admin").RequireAuthorization("Admin");
        staff.MapGet("/pilots", ListPilotsAsync);
        staff.MapPost("/pilots", CreatePilotAsync);
        admin.MapPut("/pilots/{id:guid}/password", ResetPasswordAsync);
        admin.MapPut("/pilots/{id:guid}/publication", async (Guid id, PublicationRequest input, KrylyaDb db,
            TimeProvider clock, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var pilot = await db.Pilots.FindAsync([id], ct);
            if (pilot is null)
                return Results.NotFound();
            if (pilot.PublishName != input.PublishName)
            {
                pilot.PublishName = input.PublishName;
                pilot.PublicationChangedBy = Actor(user);
                pilot.PublicationChangedAt = clock.GetUtcNow();
                db.Add(new ChangeEvent(Guid.NewGuid(), id, "Публикация имени", id,
                    input.PublishName ? "Включена" : "Отключена", Actor(user), clock.GetUtcNow()));
                await db.SaveChangesAsync(ct);
            }
            return Results.NoContent();
        });
    }

    private static async Task<IResult> LoginAsync(LoginRequest input, HttpContext context, KrylyaDb db,
        IPasswordHasher<Account> hasher, CancellationToken ct)
    {
        var login = Normalize(input.Login);
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Login == login, ct);
        if (account is null || !account.Active)
            return Results.Unauthorized();

        var verified = hasher.VerifyHashedPassword(account, account.PasswordHash, input.Password);
        if (verified == PasswordVerificationResult.Failed)
            return Results.Unauthorized();
        if (verified == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var replacement = hasher.HashPassword(account, input.Password);
            var count = await db.Accounts.Where(a => a.Id == account.Id && a.SessionVersion == account.SessionVersion)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.PasswordHash, replacement), ct);
            if (count == 0)
                return Results.Unauthorized();
        }
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Name, account.Login),
            new Claim(ClaimTypes.Role, account.Role),
            new Claim(SessionVersionClaim, account.SessionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(new ClaimsPrincipal(identity));
        return Results.Ok(new { id = account.Id, role = account.Role });
    }

    private static async Task<IResult> ListPilotsAsync(KrylyaDb db, TimeProvider clock, CancellationToken ct)
    {
        var month = CompetitionEndpoints.MonthAt(clock.GetUtcNow());
        var pilots = await db.Pilots.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ToListAsync(ct);
        var tasks = await db.Tasks.OrderBy(t => t.Stage).ThenBy(t => t.Order).Select(t => new { t.Id, t.Stage }).ToListAsync(ct);
        var stages = await db.TrainingStages.ToDictionaryAsync(s => s.Number, s => s.Title, ct);
        var passed = await db.TaskAttempts.Where(a => a.Passed == true).Select(a => new { a.PilotId, a.TaskId })
            .Distinct().ToListAsync(ct);
        var completed = passed.ToLookup(a => a.PilotId, a => a.TaskId);
        var practice = await db.Attendances.GroupBy(a => a.PilotId)
            .ToDictionaryAsync(g => g.Key, g => g.Sum(x => x.Minutes), ct);
        var laps = await (from lap in db.Laps
                          join challenge in db.Challenges on lap.ChallengeId equals challenge.Id
                          where challenge.Month == month
                          group lap by lap.PilotId into attempts
                          select new { PilotId = attempts.Key, Best = attempts.Min(a => a.Milliseconds) })
            .ToDictionaryAsync(row => row.PilotId, row => (int?)row.Best, ct);
        return Results.Ok(pilots.Select(p =>
        {
            var done = completed[p.Id].ToHashSet();
            var stage = tasks.FirstOrDefault(t => !done.Contains(t.Id))?.Stage ?? tasks.LastOrDefault()?.Stage;
            var atStage = tasks.Where(t => t.Stage == stage).ToList();
            return new
            {
                p.Id, p.FirstName, p.LastName, p.PublishName, stage,
                stageTitle = stage is int number ? stages.GetValueOrDefault(number) : null,
                stageCompleted = atStage.Count(t => done.Contains(t.Id)), stageTotal = atStage.Count,
                practiceMinutes = p.OpeningPracticeMinutes + practice.GetValueOrDefault(p.Id), bestLapMilliseconds = laps.GetValueOrDefault(p.Id)
            };
        }));
    }

    private static async Task<IResult> ResetPasswordAsync(Guid id, PasswordRequest input, KrylyaDb db,
        IPasswordHasher<Account> hasher, CancellationToken ct)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == id && a.Role == Roles.Pilot, ct);
        if (account is null)
            return Results.NotFound();
        var hash = hasher.HashPassword(account, input.Password);
        await db.Accounts.Where(a => a.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.PasswordHash, hash)
                .SetProperty(a => a.SessionVersion, a => a.SessionVersion + 1), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CreatePilotAsync(PilotRequest input, KrylyaDb db,
        IPasswordHasher<Account> hasher, CancellationToken ct)
    {
        var login = Normalize(input.Login);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(1, hashtext({login}))", ct);
        if (await db.Accounts.AnyAsync(a => a.Login == login, ct))
            return Results.Conflict(new { message = "Логин уже занят." });

        var account = new Account { Login = login, Role = Roles.Pilot };
        account.PasswordHash = hasher.HashPassword(account, input.Password);
        var pilot = new Pilot { Id = account.Id, FirstName = input.FirstName.Trim(), LastName = input.LastName.Trim() };
        db.AddRange(account, pilot);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created(uri: (string?)null, value: pilot);
    }

    public static async Task BootstrapAsync(IServiceProvider services, IConfiguration config, string role)
    {
        var login = Normalize(config["Bootstrap:Login"] ?? "");
        var password = config["Bootstrap:Password"] ?? "";
        if (login.Length is < 3 or > 64 || password.Length is < 12 or > 128)
            throw new InvalidOperationException("Set Bootstrap__Login (3–64 chars) and Bootstrap__Password (12–128 chars).");
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KrylyaDb>();
        await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(1, hashtext({login}))");
        var existing = await db.Accounts.SingleOrDefaultAsync(a => a.Login == login);
        if (existing is not null)
        {
            if (existing.Role != role)
                throw new InvalidOperationException("Login belongs to a different role.");
            Console.WriteLine("Account already exists.");
            return;
        }
        var account = new Account { Login = login, Role = role };
        account.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Account>>().HashPassword(account, password);
        db.Add(account);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        Console.WriteLine("Account created.");
    }

    internal static Guid Actor(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static string Normalize(string login) => login.Trim().ToLowerInvariant();
}

public sealed record LoginRequest(
    [Required, StringLength(64)] string Login,
    [Required, StringLength(128)] string Password);

public sealed record PilotRequest(
    [Required, StringLength(64, MinimumLength = 3)] string Login,
    [Required, StringLength(128, MinimumLength = 8)] string Password,
    [Required, StringLength(100)] string FirstName,
    [Required, StringLength(100)] string LastName);

public sealed record PublicationRequest(bool PublishName);
public sealed record PasswordRequest([Required, StringLength(128, MinimumLength = 8)] string Password);
