using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Krylya.Api;

internal static class PilotHistory
{
    public static void MapHistory(this WebApplication app)
    {
        app.MapGet("/api/staff/pilots/{id:guid}/history", HistoryAsync).RequireAuthorization("Staff");
        app.MapPut("/api/admin/pilots/{pilotId:guid}/void/{kind}/{id:guid}", VoidAsync).RequireAuthorization("Admin");
    }

    private sealed record Entry(Guid Id, string Kind, string Detail, DateTimeOffset At, Guid Actor, bool CanVoid);
    private static async Task<IResult> HistoryAsync(Guid id, KrylyaDb db, CancellationToken ct)
    {
        if (!await db.Pilots.AnyAsync(p => p.Id == id, ct)) return Results.NotFound();
        var rows = new List<Entry>();
        var month = CompetitionEndpoints.MonthAt(DateTimeOffset.UtcNow);
        var laps = await (from l in db.Laps.IgnoreQueryFilters() join c in db.Challenges on l.ChallengeId equals c.Id
                          where l.PilotId == id select new { Lap = l, c.Month, c.Track }).ToListAsync(ct);
        rows.AddRange(laps.Select(x => new Entry(x.Lap.Id, "lap", $"{x.Track} · {TimeSpan.FromMilliseconds(x.Lap.Milliseconds):m\\:ss\\.fff}",
            x.Lap.CreatedAt, x.Lap.CreatedBy, x.Lap.VoidedAt == null && x.Month == month)));
        var attempts = await db.TaskAttempts.IgnoreQueryFilters().Where(a => a.PilotId == id).ToListAsync(ct);
        foreach (var a in attempts)
        {
            rows.Add(new(a.Id, "attempt", $"{a.Title} · {a.Result}", a.CreatedAt, a.CreatedBy, a.VoidedAt == null));
            if (a.ConfirmedAt is { } at && a.ConfirmedBy is { } by)
                rows.Add(new(a.Id, "Решение", a.Passed == true ? "Выполнено" : "Не выполнено", at, by, false));
        }
        var visits = await db.Attendances.Where(a => a.PilotId == id).ToListAsync(ct);
        rows.AddRange(visits.Select(a => new Entry(a.Id, "Посещение", $"{a.Date:dd.MM.yyyy} · {a.Minutes} мин", a.CreatedAt, a.CreatedBy, false)));
        rows.AddRange(await db.ChangeEvents.Where(e => e.PilotId == id)
            .Select(e => new Entry(e.Id, e.Kind, e.Details, e.CreatedAt, e.Actor, false)).ToListAsync(ct));
        var actors = rows.Select(e => e.Actor).Distinct().ToArray();
        var names = await db.Accounts.Where(a => actors.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Login, ct);
        return Results.Ok(rows.OrderByDescending(e => e.At).Select(e => new
        { e.Id, e.Kind, e.Detail, e.At, actor = names.GetValueOrDefault(e.Actor), e.CanVoid }));
    }

    private static async Task<IResult> VoidAsync(Guid pilotId, string kind, Guid id, Correction input,
        KrylyaDb db, ClaimsPrincipal user, TimeProvider clock, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await MonthlyCompetition.LockAsync(db, ct);
        var now = clock.GetUtcNow();
        if (kind == "lap")
        {
            var lap = await db.Laps.IgnoreQueryFilters().SingleOrDefaultAsync(l => l.Id == id && l.PilotId == pilotId, ct);
            if (lap is null) return Results.NotFound();
            if (lap.VoidedAt != null) return Results.NoContent();
            if (!await db.Challenges.AnyAsync(c => c.Id == lap.ChallengeId && c.Month == CompetitionEndpoints.MonthAt(now), ct))
                return Results.Conflict(new { message = "Архив месяца зафиксирован." });
            lap.VoidedAt = now;
        }
        else if (kind == "attempt")
        {
            var attempt = await db.TaskAttempts.IgnoreQueryFilters().SingleOrDefaultAsync(a => a.Id == id && a.PilotId == pilotId, ct);
            if (attempt is null) return Results.NotFound();
            if (attempt.VoidedAt != null) return Results.NoContent();
            attempt.VoidedAt = now;
        }
        else return Results.NotFound();
        db.Add(new ChangeEvent(Guid.NewGuid(), pilotId, kind == "lap" ? "Круг аннулирован" : "Попытка аннулирована",
            id, input.Reason.Trim(), AuthEndpoints.Actor(user), now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }
}
public sealed record Correction([Required, StringLength(500)] string Reason);
