using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace Krylya.Api;

internal sealed class MonthlyCompetition(KrylyaDb db, TimeProvider clock)
{
    public static Task LockAsync(KrylyaDb db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(4, 1)", ct);

    public async Task ReconcileAsync(CancellationToken ct)
    {
        var month = CompetitionEndpoints.MonthAt(clock.GetUtcNow());
        var needsArchive = await db.Challenges.AnyAsync(c => c.Month < month && c.FinalizedAt == null, ct);
        if (!needsArchive && await db.Challenges.AnyAsync(c => c.Month == month, ct))
            return;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, ct);
        var now = clock.GetUtcNow();
        month = CompetitionEndpoints.MonthAt(now);
        var closed = await db.Challenges.Where(c => c.Month < month && c.FinalizedAt == null).ToListAsync(ct);
        foreach (var challenge in closed)
        {
            var closesAt = CompetitionEndpoints.ClosesAt(challenge.Month);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ChallengeStandings" ("ChallengeId", "PilotId", "Place", "Milliseconds", "GapMilliseconds")
                SELECT {challenge.Id}, best."PilotId", (rank() OVER (ORDER BY best."Milliseconds"))::integer,
                       best."Milliseconds", best."Milliseconds" - min(best."Milliseconds") OVER ()
                FROM (
                    SELECT "PilotId", min("Milliseconds") AS "Milliseconds"
                    FROM "Laps" WHERE "VoidedAt" IS NULL AND "ChallengeId" = {challenge.Id} AND "CreatedAt" < {closesAt}
                    GROUP BY "PilotId"
                ) best
                """, ct);
            challenge.FinalizedAt = now;
        }

        if (!await db.Challenges.AnyAsync(c => c.Month == month, ct))
        {
            var eligible = await (from pair in db.ChallengePairs.Where(p => p.Enabled)
                                  join car in db.Vehicles on pair.VehicleId equals car.Id
                                  join track in db.Circuits on pair.CircuitId equals track.Id
                                  select new { Pair = pair, car.Name, Track = track }).ToListAsync(ct);
            if (eligible.Count > 0)
            {
                var draw = eligible[RandomNumberGenerator.GetInt32(eligible.Count)];
                db.Add(new Challenge(Guid.NewGuid(), month, draw.Pair.Simulator, draw.Name, draw.Track.Label, draw.Pair.Rules)
                {
                    SourcePairId = draw.Pair.Id,
                    UpdatedAt = now
                });
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}

internal sealed class MonthlyCompetitionWorker(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<MonthlyCompetitionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<MonthlyCompetition>().ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Could not reconcile monthly challenges.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
