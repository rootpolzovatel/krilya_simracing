using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Krylya.Api;

internal static partial class TrainingEndpoints
{
    public static void MapTraining(this WebApplication app)
    {
        var staff = app.MapGroup("/api/staff").RequireAuthorization("Staff");
        var admin = app.MapGroup("/api/admin").RequireAuthorization("Admin");
        staff.MapGet("/pilots/{id:guid}/training", GetPilotAsync);
        staff.MapPost("/pilots/{id:guid}/attendance", AttendAsync);
        staff.MapPost("/pilots/{id:guid}/attempts", RecordAsync);
        staff.MapGet("/training/instructor", async (KrylyaDb db, CancellationToken ct) =>
            await db.Tasks.Where(t => t.ResultType == "time").OrderBy(t => t.Stage).ThenBy(t => t.Order).ToListAsync(ct));
        staff.MapPut("/training/instructor/{taskId:guid}", SaveInstructorLapAsync);
        staff.MapPut("/pilots/{id:guid}/tasks/{taskId:guid}/unlock", UnlockAsync);
        staff.MapPut("/attempts/{id:guid}/decision", DecideAsync);
        ScheduleEndpoints.MapSchedule(staff);
        admin.MapGet("/tasks", async (KrylyaDb db, CancellationToken ct) =>
            await db.Tasks.OrderBy(t => t.Stage).ThenBy(t => t.Order).ToListAsync(ct));
        admin.MapGet("/ranks", async (KrylyaDb db, CancellationToken ct) =>
            await db.RankRules.OrderBy(r => r.Order).ToListAsync(ct));
        admin.MapPost("/tasks", CreateTaskAsync);
        admin.MapPut("/tasks/{id:guid}", EditTaskAsync);
        admin.MapGet("/stages", async (KrylyaDb db, CancellationToken ct) =>
            await db.TrainingStages.OrderBy(s => s.Number).Select(s => new { s.Number, s.Title }).ToListAsync(ct));
        admin.MapPut("/stages/{number:int}", NameStageAsync);
        admin.MapPost("/ranks", CreateRankAsync);
        admin.MapPut("/ranks/{id:guid}", EditRankAsync);
        app.MapGet("/api/pilot/me", (ClaimsPrincipal user, KrylyaDb db, TimeProvider clock, CancellationToken ct) =>
            GetPilotAsync(AuthEndpoints.Actor(user), db, clock, ct)).RequireAuthorization("Pilot");
        app.MapGet("/api/pilot/tasks/{taskId:guid}/standings", GetTaskStandingsAsync).RequireAuthorization("Pilot");
    }

    private static async Task<IResult> GetTaskStandingsAsync(Guid taskId, ClaimsPrincipal user, KrylyaDb db,
        CancellationToken ct)
    {
        var task = await db.Tasks.FindAsync([taskId], ct);
        if (task is null || task.ResultType is not ("time" or "points" or "place"))
            return Results.NotFound();

        var viewerId = AuthEndpoints.Actor(user);
        var attempts = await (from attempt in db.TaskAttempts
                              join pilot in db.Pilots on attempt.PilotId equals pilot.Id
                              where attempt.TaskId == taskId && attempt.Passed == true
                                  && attempt.ResultType == task.ResultType
                              select new { pilot.Id, pilot.FirstName, pilot.LastName, attempt.Result, attempt.ResultMilliseconds })
            .ToListAsync(ct);

        var lowerIsBetter = task.ResultType != "points";
        var best = attempts.Select(a => new
        {
            a.Id, a.FirstName, a.LastName,
            Value = ReadStandingValue(task.ResultType, a.Result, a.ResultMilliseconds)
        })
            .Where(a => a.Value.HasValue)
            .GroupBy(a => a.Id)
            .Select(group => group.OrderBy(a => lowerIsBetter ? a.Value : -a.Value).First())
            .OrderBy(a => lowerIsBetter ? a.Value : -a.Value)
            .ThenBy(a => a.FirstName)
            .ThenBy(a => a.LastName)
            .ThenBy(a => a.Id)
            .ToArray();

        var standings = new List<object>(best.Length);
        var place = 0;
        for (var i = 0; i < best.Length; i++)
        {
            var entry = best[i];
            if (i == 0 || entry.Value != best[i - 1].Value)
                place = i + 1;
            var lastInitial = StringInfo.GetNextTextElement(entry.LastName);
            standings.Add(new
            {
                place,
                name = lastInitial.Length > 0 ? $"{entry.FirstName} {lastInitial}." : entry.FirstName,
                result = entry.Value!.Value.ToString("0.######", CultureInfo.InvariantCulture),
                isSelf = entry.Id == viewerId
            });
        }
        return Results.Ok(new
        {
            task.Title, task.Normative, task.ResultType,
            InstructorLapMilliseconds = task.ResultType == "time" ? task.InstructorLapMilliseconds : null,
            standings
        });
    }

    private static async Task<IResult> SaveInstructorLapAsync(Guid taskId, InstructorTaskResultRequest input,
        KrylyaDb db, CancellationToken ct)
    {
        var task = await db.Tasks.FindAsync([taskId], ct);
        if (task is null || task.ResultType != "time")
            return Results.NotFound();
        if (!IsValidResult(task, input.Result.Trim(), out var milliseconds))
            return Results.BadRequest(new { message = "Укажите время в формате 1:39.500." });

        var updated = await db.Tasks.Where(t => t.Id == taskId && t.ResultType == "time")
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.InstructorLapMilliseconds, milliseconds), ct);
        return updated == 1 ? Results.NoContent() : Results.NotFound();
    }

    private static decimal? ReadStandingValue(string type, string result, long? milliseconds) => type switch
    {
        "time" => milliseconds > 0 ? milliseconds.Value : null,
        "points" => decimal.TryParse(result.Replace(',', '.'),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var points)
            ? points : null,
        "place" => int.TryParse(result, NumberStyles.None, CultureInfo.InvariantCulture, out var place) && place > 0
            ? place : null,
        _ => null
    };

    private static async Task<IResult> GetPilotAsync(Guid id, KrylyaDb db, TimeProvider clock, CancellationToken ct)
    {
        var pilot = await db.Pilots.FindAsync([id], ct);
        if (pilot is null)
            return Results.NotFound();
        var attempts = await db.TaskAttempts.Where(a => a.PilotId == id).OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
        var tasks = await db.Tasks.OrderBy(t => t.Stage).ThenBy(t => t.Order).ToListAsync(ct);
        var completed = attempts.Where(a => a.Passed == true).Select(a => a.TaskId).ToHashSet();
        var remaining = tasks.Where(t => !completed.Contains(t.Id)).Take(2).ToList();
        var activeTask = remaining.FirstOrDefault();
        var nextTask = remaining.Skip(1).FirstOrDefault();
        var unlocked = await db.TaskUnlocks.Where(u => u.PilotId == id).Select(u => u.TaskId).ToListAsync(ct);
        var completedStages = tasks.GroupBy(t => t.Stage).Where(g => g.All(t => completed.Contains(t.Id)))
            .Select(g => g.Key).ToHashSet();
        var ranks = await db.RankRules.OrderByDescending(r => r.Order).ToListAsync(ct);
        var rank = ranks.FirstOrDefault(r => completed.Count >= r.MinimumCompletedTasks
            && (r.MinimumCompletedStage == null || (completedStages.Contains(r.MinimumCompletedStage.Value)
                && tasks.Where(t => t.Stage <= r.MinimumCompletedStage).All(t => completed.Contains(t.Id))))
            && r.RequiredTaskIds.All(completed.Contains));
        var minutes = await db.Attendances.Where(a => a.PilotId == id).SumAsync(a => a.Minutes, ct);
        var now = clock.GetUtcNow();
        var month = CompetitionEndpoints.MonthAt(now);
        var bestLapMilliseconds = await (from lap in db.Laps
                                        join challenge in db.Challenges on lap.ChallengeId equals challenge.Id
                                        where lap.PilotId == id && challenge.Month == month
                                        select (int?)lap.Milliseconds).MinAsync(ct);
        var stage = activeTask?.Stage ?? tasks.LastOrDefault()?.Stage;
        var stageTitle = await db.TrainingStages.Where(s => s.Number == stage).Select(s => s.Title).SingleOrDefaultAsync(ct);
        return Results.Ok(new
        {
            pilot.Id, pilot.FirstName, pilot.LastName, practiceMinutes = pilot.OpeningPracticeMinutes + minutes, bestLapMilliseconds,
            stage, stageTitle,
            rank = rank?.Title ?? "Новичок",
            tasks = tasks.Select(t => new
            {
                task = t, completed = completed.Contains(t.Id),
                open = completed.Contains(t.Id) || t.Id == activeTask?.Id || unlocked.Contains(t.Id),
                manuallyOpened = unlocked.Contains(t.Id), canUnlock = t.Id == nextTask?.Id && !unlocked.Contains(t.Id),
                attempts = attempts.Where(a => a.TaskId == t.Id)
                    .Select(a => new { a.Id, a.Result, a.ResultType, a.ResultMilliseconds, a.PointsPrecision,
                        a.Title, a.Instructions, a.Normative, a.Passed, a.CreatedAt, a.ConfirmedAt })
            })
        });
    }

    private static async Task<IResult> AttendAsync(Guid id, AttendanceRequest input, KrylyaDb db, TimeProvider clock,
        ClaimsPrincipal user, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), CompetitionEndpoints.Moscow).DateTime);
        if (input.OperationId == Guid.Empty || input.Date.Year < 2000 || input.Date > today)
            return Results.BadRequest(new { message = "Укажите дату фактического посещения. Она не может быть в будущем." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(3, hashtext({input.OperationId.ToString()}))", ct);
        var existing = await db.Attendances.FindAsync([input.OperationId], ct);
        if (existing is not null)
            return existing.PilotId == id && existing.Date == input.Date && existing.BookingId == input.BookingId && existing.Minutes == input.Minutes
                ? Results.Ok(existing) : Results.Conflict();
        if (!await db.Pilots.AnyAsync(p => p.Id == id, ct))
            return Results.NotFound();
        if (input.BookingId is Guid bookingId)
        {
            var slot = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId, ct);
            if (slot is null || slot.PilotId != id)
                return Results.BadRequest(new { message = "Запись в расписании не соответствует ученику." });
            await ScheduleEndpoints.LockSlotAsync(db, slot.Weekday, slot.StartHour, ct);
            var booking = await db.Bookings.Include(b => b.Skips).SingleAsync(b => b.Id == bookingId, ct);
            var previous = await db.Attendances.SingleOrDefaultAsync(a => a.BookingId == bookingId && a.Date == input.Date, ct);
            if (previous is not null)
                return previous.Minutes == input.Minutes ? Results.Ok(previous)
                    : Results.Conflict(new { message = "Занятие уже отмечено с другой длительностью." });
            if ((int)input.Date.DayOfWeek != booking.Weekday || !ScheduleEndpoints.Occurs(booking, input.Date))
                return Results.Conflict(new { message = "На эту дату занятие не запланировано." });
        }
        var attendance = new Attendance(input.OperationId, id, input.Date, input.Minutes, AuthEndpoints.Actor(user), clock.GetUtcNow()) { BookingId = input.BookingId };
        db.Add(attendance);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created(uri: (string?)null, value: attendance);
    }

    private static async Task<IResult> RecordAsync(Guid id, TaskResultRequest input, KrylyaDb db, TimeProvider clock,
        ClaimsPrincipal user, CancellationToken ct)
    {
        var attemptId = input.OperationId ?? Guid.NewGuid();
        if (attemptId == Guid.Empty)
            return Results.BadRequest();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(5, hashtext({attemptId.ToString()}))", ct);
        var result = input.Result.Trim();
        var existing = await db.TaskAttempts.IgnoreQueryFilters().SingleOrDefaultAsync(a => a.Id == attemptId, ct);
        if (existing is not null)
            return existing.VoidedAt == null && existing.PilotId == id && existing.TaskId == input.TaskId && existing.Result == result
                ? Results.Ok(existing) : Results.Conflict();
        var task = await db.Tasks.FindAsync([input.TaskId], ct);
        if (task is null || !await db.Pilots.AnyAsync(p => p.Id == id, ct))
            return Results.NotFound();
        if (!IsValidResult(task, result, out var milliseconds))
            return Results.BadRequest(new { message = "Формат результата не соответствует заданию." });
        var priorIds = db.Tasks.Where(t => t.Stage < task.Stage || (t.Stage == task.Stage && t.Order < task.Order));
        if (!await db.TaskUnlocks.AnyAsync(u => u.PilotId == id && u.TaskId == task.Id, ct)
            && !await db.TaskAttempts.AnyAsync(a => a.PilotId == id && a.TaskId == task.Id && a.Passed == true, ct)
            && await priorIds.AnyAsync(t => !db.TaskAttempts.Any(a => a.PilotId == id && a.TaskId == t.Id && a.Passed == true), ct))
            return Results.Conflict(new { message = "Сначала подтвердите предыдущие задания." });
        var attempt = new TaskAttempt
        {
            Id = attemptId, PilotId = id, TaskId = task.Id, Result = result, ResultMilliseconds = milliseconds,
            PointsPrecision = task.PointsPrecision, Title = task.Title, Instructions = task.Instructions,
            ResultType = task.ResultType, Normative = task.Normative, CreatedBy = AuthEndpoints.Actor(user), CreatedAt = clock.GetUtcNow()
        };
        db.Add(attempt);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Created(uri: (string?)null, value: attempt);
    }

    private static async Task<IResult> NameStageAsync(int number, StageNameRequest input, KrylyaDb db,
        TimeProvider clock, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!await db.Tasks.AnyAsync(t => t.Stage == number, ct))
            return Results.NotFound();
        var title = input.Title.Trim();
        var by = AuthEndpoints.Actor(user);
        var now = clock.GetUtcNow();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TrainingStages" ("Number", "Title", "UpdatedBy", "UpdatedAt")
            VALUES ({number}, {title}, {by}, {now})
            ON CONFLICT ("Number") DO UPDATE SET "Title" = EXCLUDED."Title",
                "UpdatedBy" = EXCLUDED."UpdatedBy", "UpdatedAt" = EXCLUDED."UpdatedAt"
            """, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UnlockAsync(Guid id, Guid taskId, KrylyaDb db, TimeProvider clock,
        ClaimsPrincipal user, CancellationToken ct)
    {
        if (!await db.Pilots.AnyAsync(p => p.Id == id, ct))
            return Results.NotFound();
        if (await db.TaskUnlocks.AnyAsync(u => u.PilotId == id && u.TaskId == taskId, ct))
            return Results.NoContent();
        var unfinished = await db.Tasks
            .Where(t => !db.TaskAttempts.Any(a => a.PilotId == id && a.TaskId == t.Id && a.Passed == true))
            .OrderBy(t => t.Stage).ThenBy(t => t.Order).Select(t => t.Id).Take(2).ToListAsync(ct);
        if (unfinished.Count < 2 || unfinished[1] != taskId)
            return Results.Conflict(new { message = "Вручную можно открыть только ближайшее следующее задание." });
        var by = AuthEndpoints.Actor(user);
        var now = clock.GetUtcNow();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TaskUnlocks" ("PilotId", "TaskId", "CreatedBy", "CreatedAt")
            VALUES ({id}, {taskId}, {by}, {now}) ON CONFLICT ("PilotId", "TaskId") DO NOTHING
            """, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DecideAsync(Guid id, DecisionRequest input, KrylyaDb db, TimeProvider clock,
        ClaimsPrincipal user, CancellationToken ct)
    {
        var updated = await db.TaskAttempts.Where(a => a.Id == id && a.Passed == null)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Passed, input.Passed)
                .SetProperty(a => a.ConfirmedBy, AuthEndpoints.Actor(user))
                .SetProperty(a => a.ConfirmedAt, clock.GetUtcNow()), ct);
        if (updated == 1)
            return Results.NoContent();
        var attempt = await db.TaskAttempts.FindAsync([id], ct);
        if (attempt is null)
            return Results.NotFound();
        return attempt.Passed == input.Passed ? Results.NoContent() : Results.Conflict();
    }

    private static Task<IResult> CreateTaskAsync(TaskRequest input, KrylyaDb db, TimeProvider clock,
        ClaimsPrincipal user, CancellationToken ct) => SaveTaskAsync(null, input, db, clock, user, ct);

    private static async Task<IResult> EditTaskAsync(Guid id, TaskRequest input, KrylyaDb db, TimeProvider clock,
        ClaimsPrincipal user, CancellationToken ct)
    {
        var existing = await db.Tasks.FindAsync([id], ct);
        return existing is null ? Results.NotFound() : await SaveTaskAsync(existing, input, db, clock, user, ct);
    }

    private static async Task<IResult> SaveTaskAsync(TrainingTask? existing, TaskRequest input, KrylyaDb db,
        TimeProvider clock, ClaimsPrincipal user, CancellationToken ct)
    {
        if (input.ResultType is not ("time" or "points" or "place" or "manual"))
            return Results.BadRequest(new { message = "Неизвестный тип результата." });
        var task = new TrainingTask(existing?.Id ?? Guid.NewGuid(), input.Stage, input.Order, input.Title.Trim(),
            input.Instructions.Trim(), input.ResultType, input.Normative.Trim(),
            input.ResultType == "points" ? input.PointsPrecision ?? 2 : null)
        {
            UpdatedBy = AuthEndpoints.Actor(user), UpdatedAt = clock.GetUtcNow(),
            InstructorLapMilliseconds = existing?.InstructorLapMilliseconds
        };
        if (existing is null)
            db.Add(task);
        else
            db.Entry(existing).CurrentValues.SetValues(task);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { return Results.Conflict(new { message = "Позиция задания на этом этапе уже занята." }); }
        return existing is null ? Results.Created(uri: (string?)null, value: task) : Results.Ok(task);
    }

    private static Task<IResult> CreateRankAsync(RankRequest input, KrylyaDb db, TimeProvider clock,
        ClaimsPrincipal user, CancellationToken ct) => SaveRankAsync(null, input, db, clock, user, ct);

    private static async Task<IResult> EditRankAsync(Guid id, RankRequest input, KrylyaDb db, TimeProvider clock,
        ClaimsPrincipal user, CancellationToken ct)
    {
        var existing = await db.RankRules.FindAsync([id], ct);
        return existing is null ? Results.NotFound() : await SaveRankAsync(existing, input, db, clock, user, ct);
    }

    private static async Task<IResult> SaveRankAsync(RankRule? existing, RankRequest input, KrylyaDb db,
        TimeProvider clock, ClaimsPrincipal user, CancellationToken ct)
    {
        var ids = input.RequiredTaskIds.Distinct().ToArray();
        if (await db.Tasks.CountAsync(t => ids.Contains(t.Id), ct) != ids.Length)
            return Results.BadRequest(new { message = "Среди обязательных есть неизвестные задания." });
        if (input.MinimumCompletedStage is int stage && !await db.Tasks.AnyAsync(t => t.Stage == stage, ct))
            return Results.BadRequest(new { message = "На указанном этапе пока нет заданий." });
        var rank = new RankRule(existing?.Id ?? Guid.NewGuid(), input.Order, input.Title.Trim(), input.MinimumCompletedTasks, ids,
            input.MinimumCompletedStage) { UpdatedBy = AuthEndpoints.Actor(user), UpdatedAt = clock.GetUtcNow() };
        if (existing is null)
            db.Add(rank);
        else
            db.Entry(existing).CurrentValues.SetValues(rank);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { return Results.Conflict(new { message = "Позиция звания уже занята." }); }
        return existing is null ? Results.Created(uri: (string?)null, value: rank) : Results.Ok(rank);
    }

    private static bool IsValidResult(TrainingTask task, string value, out long? milliseconds)
    {
        milliseconds = null;
        if (task.ResultType == "time")
        {
            if (!LapTimePattern().IsMatch(value))
                return false;
            var colon = value.IndexOf(':');
            var time = int.Parse(value.AsSpan(0, colon), CultureInfo.InvariantCulture) * 60_000L
                + int.Parse(value.AsSpan(colon + 1, 2), CultureInfo.InvariantCulture) * 1000
                + int.Parse(value.AsSpan(colon + 4), CultureInfo.InvariantCulture);
            milliseconds = time;
            return time > 0;
        }
        if (task.ResultType == "points")
        {
            var normalized = value.Replace(',', '.');
            var separator = normalized.IndexOf('.');
            return (separator < 0 || normalized.Length - separator - 1 <= (task.PointsPrecision ?? 2))
                && decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out _);
        }
        return task.ResultType == "place"
            ? int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var place) && place > 0
            : task.ResultType == "manual" && value.Length > 0;
    }

    [GeneratedRegex(@"^(0|[1-9][0-9]{0,4}):[0-5][0-9]\.[0-9]{3}$")]
    private static partial Regex LapTimePattern();
}

public sealed record AttendanceRequest(Guid OperationId, DateOnly Date, [Range(1, 1440)] int Minutes = 60, Guid? BookingId = null);
public sealed record TaskResultRequest(Guid TaskId, [Required, StringLength(1000)] string Result, Guid? OperationId = null);
public sealed record InstructorTaskResultRequest([Required, StringLength(1000)] string Result);
public sealed record StageNameRequest([Required, StringLength(100)] string Title);
public sealed record DecisionRequest([property: System.Text.Json.Serialization.JsonRequired] bool Passed);
public sealed record TaskRequest([Range(1, 1000)] int Stage, [Range(1, 10000)] int Order,
    [Required, StringLength(150)] string Title, [Required, StringLength(5000)] string Instructions,
    [Required] string ResultType, [Required, StringLength(1000)] string Normative, [Range(0, 6)] int? PointsPrecision = null);
public sealed record RankRequest([Range(1, 1000)] int Order, [Required, StringLength(100)] string Title,
    [Range(0, 10000)] int MinimumCompletedTasks, [Required, MaxLength(1000)] Guid[] RequiredTaskIds,
    [Range(1, 1000)] int? MinimumCompletedStage = null);
