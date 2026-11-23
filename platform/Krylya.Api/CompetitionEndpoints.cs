using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Krylya.Api;

internal static class CompetitionEndpoints
{
    internal static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

    public static void MapCompetition(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization("Admin");
        admin.MapGet("/challenges", async (KrylyaDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var current = MonthAt(clock.GetUtcNow());
            return await db.Challenges.OrderByDescending(c => c.Month)
                .Select(c => new
                {
                    c.Id, c.Month, c.Simulator, c.Car, c.Track, c.Rules, c.SourcePairId,
                    canEdit = c.Month >= current && !db.Laps.IgnoreQueryFilters().Any(l => l.ChallengeId == c.Id)
                }).ToListAsync(ct);
        });
        admin.MapPost("/challenges", CreateChallengeAsync);
        admin.MapPut("/challenges/{id:guid}", EditChallengeAsync);
        ChallengeCatalogEndpoints.MapCatalog(admin);
        ChallengeImagesEndpoints.MapChallengeImages(app, admin);

        var staff = app.MapGroup("/api/staff").RequireAuthorization("Staff");
        staff.MapPost("/challenges/{id:guid}/laps", RecordLapAsync);
        staff.MapGet("/challenges/current", (KrylyaDb db, TimeProvider clock, CancellationToken ct) =>
            GetResultsAsync(MonthAt(clock.GetUtcNow()), false, db, ct));

        var publicApi = app.MapGroup("/api/public");
        publicApi.MapGet("/leaderboard", (KrylyaDb db, TimeProvider clock, CancellationToken ct) =>
            GetResultsAsync(MonthAt(clock.GetUtcNow()), true, db, ct));
        publicApi.MapGet("/archive", async (KrylyaDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var current = MonthAt(clock.GetUtcNow());
            return await db.Challenges.Where(c => c.Month < current && c.FinalizedAt != null)
                .OrderByDescending(c => c.Month)
                .Select(c => new
                {
                    c.Month, c.Car, c.Track, c.Simulator,
                    bestLapMilliseconds = db.ChallengeStandings.Where(s => s.ChallengeId == c.Id
                        && db.Pilots.Any(p => p.Id == s.PilotId && p.PublishName)).Min(s => (int?)s.Milliseconds)
                }).ToListAsync(ct);
        });
        publicApi.MapGet("/archive/{month}", (DateOnly month, KrylyaDb db, TimeProvider clock, CancellationToken ct) =>
            month < MonthAt(clock.GetUtcNow()) && month.Day == 1
                ? GetResultsAsync(month, true, db, ct, archivedOnly: true) : Task.FromResult(Results.NotFound()));
    }

    private static async Task<IResult> CreateChallengeAsync(ChallengeRequest input, KrylyaDb db, TimeProvider clock,
        ClaimsPrincipal user, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await MonthlyCompetition.LockAsync(db, ct);
        var now = clock.GetUtcNow();
        if (input.Month.Day != 1 || input.Month < MonthAt(now) || input.Month.Year > 9998)
            return Results.BadRequest(new { message = "Укажите первое число текущего или будущего месяца." });
        if (await db.Challenges.AnyAsync(c => c.Month == input.Month, ct))
            return Results.Conflict(new { message = "На этот месяц уже есть испытание." });
        var challenge = new Challenge(Guid.NewGuid(), input.Month, input.Simulator.Trim(), input.Car.Trim(),
            input.Track.Trim(), input.Rules.Trim()) { UpdatedBy = AuthEndpoints.Actor(user), UpdatedAt = now };
        db.Add(challenge);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created(uri: (string?)null, value: challenge);
    }

    private static async Task<IResult> EditChallengeAsync(Guid id, ChallengeRequest input, KrylyaDb db,
        TimeProvider clock, ClaimsPrincipal user, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await MonthlyCompetition.LockAsync(db, ct);
        var challenge = await db.Challenges.FindAsync([id], ct);
        if (challenge is null)
            return Results.NotFound();
        var now = clock.GetUtcNow();
        if (input.Month != challenge.Month || challenge.Month < MonthAt(now)
            || await db.Laps.IgnoreQueryFilters().AnyAsync(l => l.ChallengeId == id, ct))
            return Results.Conflict(new { message = "Условия можно менять до первого записанного круга." });
        var car = input.Car.Trim();
        var track = input.Track.Trim();
        var removeCar = challenge.Car != car;
        var removeTrack = challenge.Track != track;
        if (removeCar || removeTrack)
            await db.ChallengeImages.Where(i => i.ChallengeId == id
                && ((removeCar && i.Kind == "car") || (removeTrack && i.Kind == "track"))).ExecuteDeleteAsync(ct);
        var updated = challenge with
        {
            Simulator = input.Simulator.Trim(), Car = car, Track = track, Rules = input.Rules.Trim(),
            UpdatedBy = AuthEndpoints.Actor(user), UpdatedAt = now
        };
        db.Entry(challenge).CurrentValues.SetValues(updated);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(updated);
    }

    private static async Task<IResult> RecordLapAsync(Guid id, LapRequest input, KrylyaDb db, TimeProvider clock,
        ClaimsPrincipal user, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await MonthlyCompetition.LockAsync(db, ct);
        var challenge = await db.Challenges.FindAsync([id], ct);
        if (challenge is null || !await db.Pilots.AnyAsync(p => p.Id == input.PilotId, ct))
            return Results.NotFound();
        var lapId = input.OperationId ?? Guid.NewGuid();
        if (lapId == Guid.Empty)
            return Results.BadRequest();
        var existing = await db.Laps.IgnoreQueryFilters().SingleOrDefaultAsync(l => l.Id == lapId, ct);
        if (existing is not null)
            return existing.VoidedAt == null && existing.ChallengeId == id && existing.PilotId == input.PilotId && existing.Milliseconds == input.Milliseconds
                ? Results.Ok(existing) : Results.Conflict();
        var now = clock.GetUtcNow();
        if (challenge.Month != MonthAt(now))
            return Results.Conflict(new { message = "Испытание сейчас не принимает результаты." });

        var lap = new Lap(lapId, id, input.PilotId, input.Milliseconds, AuthEndpoints.Actor(user), now);
        db.Add(lap);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created(uri: (string?)null, value: lap);
    }

    private static async Task<IResult> GetResultsAsync(DateOnly month, bool publicView, KrylyaDb db, CancellationToken ct, bool archivedOnly = false)
    {
        var challenge = await db.Challenges.SingleOrDefaultAsync(c => c.Month == month && (!archivedOnly || c.FinalizedAt != null), ct);
        if (challenge is null)
            return Results.NotFound();

        var closesAt = ClosesAt(month);
        var official = challenge.FinalizedAt is not null;
        var standings = official
            ? await (from row in db.ChallengeStandings
                     join pilot in db.Pilots on row.PilotId equals pilot.Id
                     where row.ChallengeId == challenge.Id && (!publicView || pilot.PublishName)
                     orderby row.Place, pilot.LastName, pilot.FirstName, pilot.Id
                     select new Standing(row.Place, pilot.FirstName, pilot.LastName, row.Milliseconds,
                         row.GapMilliseconds, publicView ? null : row.PilotId)).ToListAsync(ct)
            : await GetOpenStandingsAsync(challenge.Id, closesAt, publicView, db, ct);
        var imageKinds = await db.ChallengeImages.Where(i => i.ChallengeId == challenge.Id).Select(i => i.Kind).ToListAsync(ct);
        var card = new
        {
            challenge.Id, challenge.Month, challenge.Simulator, challenge.Car, challenge.Track, challenge.Rules,
            carImageUrl = imageKinds.Contains("car") ? $"/api/public/challenges/{challenge.Id}/images/car" : null,
            trackImageUrl = imageKinds.Contains("track") ? $"/api/public/challenges/{challenge.Id}/images/track" : null
        };
        return Results.Ok(new { challenge = card, closesAt, standings });
    }

    private static async Task<List<Standing>> GetOpenStandingsAsync(Guid challengeId, DateTimeOffset closesAt,
        bool publicView, KrylyaDb db, CancellationToken ct)
    {
        var best = from lap in db.Laps
                   where lap.ChallengeId == challengeId && lap.CreatedAt < closesAt
                   group lap by lap.PilotId into runs
                   select new { PilotId = runs.Key, Time = runs.Min(l => l.Milliseconds) };
        var ordered = await (from result in best
                             join pilot in db.Pilots on result.PilotId equals pilot.Id
                             orderby result.Time, pilot.LastName, pilot.FirstName, pilot.Id
                             select new { pilot.Id, pilot.FirstName, pilot.LastName, pilot.PublishName, result.Time }).ToListAsync(ct);
        var standings = new List<Standing>(ordered.Count);
        var place = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            var result = ordered[i];
            if (i == 0 || result.Time != ordered[i - 1].Time)
                place = i + 1;
            if (!publicView || result.PublishName)
                standings.Add(new Standing(place, result.FirstName, result.LastName, result.Time,
                    result.Time - ordered[0].Time, publicView ? null : result.Id));
        }
        return standings;
    }

    internal static DateOnly MonthAt(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Moscow);
        return new DateOnly(local.Year, local.Month, 1);
    }

    internal static DateTimeOffset ClosesAt(DateOnly month) =>
        TimeZoneInfo.ConvertTimeToUtc(month.AddMonths(1).ToDateTime(TimeOnly.MinValue), Moscow);

    private sealed record Standing(int Place, string FirstName, string LastName, int Milliseconds, int GapMilliseconds,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        Guid? PilotId);
}

public sealed record ChallengeRequest(DateOnly Month, [Required, StringLength(120)] string Simulator,
    [Required, StringLength(120)] string Car, [Required, StringLength(150)] string Track,
    [Required, StringLength(5000)] string Rules);

public sealed record LapRequest(Guid PilotId, [Range(1, 86_400_000)] int Milliseconds, Guid? OperationId = null);
