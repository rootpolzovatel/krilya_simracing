using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Krylya.Api;

internal static class ScheduleEndpoints
{
    public static void MapSchedule(RouteGroupBuilder staff)
    {
        staff.MapGet("/schedule", GetWeekAsync);
        staff.MapPost("/schedule", CreateAsync);
        staff.MapPut("/schedule/{id:guid}/skip/{date}", (Guid id, DateOnly date, KrylyaDb db,
            ClaimsPrincipal user, CancellationToken ct) => ChangeAsync(id, date, true, db, user, ct));
        staff.MapPut("/schedule/{id:guid}/end/{date}", (Guid id, DateOnly date, KrylyaDb db,
            ClaimsPrincipal user, CancellationToken ct) => ChangeAsync(id, date, false, db, user, ct));
    }

    private static async Task<IResult> GetWeekAsync(DateOnly week, KrylyaDb db, CancellationToken ct)
    {
        if (week.DayOfWeek != DayOfWeek.Monday || week.Year is < 2000 or > 9998)
            return Results.BadRequest(new { message = "Укажите понедельник нужной недели." });
        var end = week.AddDays(5);
        var series = await db.Bookings.AsNoTracking().Include(b => b.Skips.Where(s => s.Date >= week && s.Date < end))
            .Where(b => b.StartsOn < end && (b.EndsBefore == null || b.EndsBefore > week))
            .ToListAsync(ct);
        var attendance = (await db.Attendances.Where(a => a.BookingId != null && a.Date >= week && a.Date < end)
            .Select(a => new { a.BookingId, a.Date, a.Minutes }).ToListAsync(ct))
            .ToDictionary(a => (a.BookingId!.Value, a.Date), a => (int?)a.Minutes);
        var occurrences = series.Select(b => new { Booking = b, Date = week.AddDays(b.Weekday - 1) })
            .Where(x => Occurs(x.Booking, x.Date))
            .GroupBy(x => new { x.Date, x.Booking.StartHour })
            .OrderBy(g => g.Key.Date).ThenBy(g => g.Key.StartHour)
            .Select(g => new
            {
                g.Key.Date, g.Key.StartHour, capacity = 2,
                bookings = g.Select(x => new { x.Booking.Id, x.Booking.PilotId, minutes = attendance.GetValueOrDefault((x.Booking.Id, x.Date)) })
            });
        return Results.Ok(occurrences);
    }

    private static async Task<IResult> CreateAsync(BookingRequest input, KrylyaDb db, ClaimsPrincipal user, CancellationToken ct)
    {
        var weekday = (int)input.StartsOn.DayOfWeek;
        if (weekday is 0 or 6 || input.StartsOn.Year is < 2000 or > 9998)
            return Results.BadRequest(new { message = "Выберите будний день." });
        if (!await db.Pilots.AnyAsync(p => p.Id == input.PilotId, ct))
            return Results.NotFound();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockSlotAsync(db, weekday, input.StartHour, ct);
        var series = await db.Bookings.Include(b => b.Skips)
            .Where(b => b.Weekday == weekday && b.StartHour == input.StartHour
                && (b.EndsBefore == null || b.EndsBefore > input.StartsOn)).ToListAsync(ct);

        // The new series has no end. Occupancy can increase only at an existing start
        // or immediately after a skipped occurrence.
        var datesToCheck = series.Select(b => b.StartsOn)
            .Concat(series.SelectMany(b => b.Skips).Where(s => s.Date.Year < 9999).Select(s => s.Date.AddDays(7)))
            .Append(input.StartsOn).Where(d => d >= input.StartsOn).Distinct();
        foreach (var date in datesToCheck)
        {
            var active = series.Where(b => Occurs(b, date)).ToArray();
            if (active.Length >= 2 || active.Any(b => b.PilotId == input.PilotId))
                return Results.Conflict(new { message = "Слот занят или у ученика уже есть занятие.", date });
        }
        var booking = new Booking
        {
            PilotId = input.PilotId, StartsOn = input.StartsOn, Weekday = weekday,
            StartHour = input.StartHour, CreatedBy = AuthEndpoints.Actor(user)
        };
        db.Add(booking);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created(uri: (string?)null, value: booking);
    }

    private static async Task<IResult> ChangeAsync(Guid id, DateOnly date, bool skip, KrylyaDb db,
        ClaimsPrincipal user, CancellationToken ct)
    {
        var slot = await db.Bookings.AsNoTracking().Where(b => b.Id == id)
            .Select(b => new { b.Weekday, b.StartHour }).SingleOrDefaultAsync(ct);
        if (slot is null)
            return Results.NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockSlotAsync(db, slot.Weekday, slot.StartHour, ct);
        var booking = await db.Bookings.Include(b => b.Skips).SingleAsync(b => b.Id == id, ct);
        if ((int)date.DayOfWeek != booking.Weekday || date < booking.StartsOn)
            return Results.BadRequest(new { message = "Дата не соответствует серии." });
        if (booking.EndsBefore is null || date < booking.EndsBefore)
        {
            if (skip)
            {
                if (booking.Skips.All(s => s.Date != date))
                    db.Add(new BookingSkip(id, date, AuthEndpoints.Actor(user)));
            }
            else
                booking.EndsBefore = date;
            if (db.ChangeTracker.HasChanges())
                db.Add(new ChangeEvent(Guid.NewGuid(), booking.PilotId, skip ? "Пропуск занятия" : "Серия прекращена",
                    id, date.ToString("yyyy-MM-dd"), AuthEndpoints.Actor(user), DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    internal static bool Occurs(Booking booking, DateOnly date) =>
        booking.StartsOn <= date && (booking.EndsBefore is null || date < booking.EndsBefore)
        && booking.Skips.All(s => s.Date != date);

    internal static Task LockSlotAsync(KrylyaDb db, int weekday, int hour, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(2, {weekday * 24 + hour})", ct);
}

public sealed record BookingRequest(Guid PilotId, DateOnly StartsOn, [Range(9, 18)] int StartHour);
