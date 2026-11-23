using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Krylya.Api;

internal static class ChallengeCatalogEndpoints
{
    public static void MapCatalog(RouteGroupBuilder admin)
    {
        var catalog = admin.MapGroup("/catalog");
        catalog.MapGet("", async (KrylyaDb db, CancellationToken ct) =>
        {
            var vehicles = await db.Vehicles.OrderBy(v => v.Name).ToListAsync(ct);
            var circuits = await db.Circuits.OrderBy(t => t.Name).ThenBy(t => t.Configuration).ToListAsync(ct);
            var pairs = await db.ChallengePairs.OrderBy(p => p.Simulator).ThenBy(p => p.Id).ToListAsync(ct);
            return new { vehicles, circuits, pairs };
        });
        catalog.MapPost("/vehicles", (VehicleRequest input, KrylyaDb db, CancellationToken ct) =>
            AddAsync(new Vehicle(Guid.NewGuid(), input.Name.Trim()), db, ct));
        catalog.MapPost("/circuits", (CircuitRequest input, KrylyaDb db, CancellationToken ct) =>
            AddAsync(new Circuit(Guid.NewGuid(), input.Name.Trim(), input.Configuration?.Trim() ?? ""), db, ct));
        catalog.MapPut("/vehicles/{id:guid}", (Guid id, VehicleRequest input, KrylyaDb db, CancellationToken ct) =>
            ReplaceAsync(new Vehicle(id, input.Name.Trim()), id, db, ct));
        catalog.MapPut("/circuits/{id:guid}", (Guid id, CircuitRequest input, KrylyaDb db, CancellationToken ct) =>
            ReplaceAsync(new Circuit(id, input.Name.Trim(), input.Configuration?.Trim() ?? ""), id, db, ct));
        catalog.MapPost("/pairs", CreatePairAsync);
        catalog.MapPut("/pairs/{id:guid}", EditPairAsync);
    }

    private static async Task<IResult> CreatePairAsync(PairRequest input, KrylyaDb db, ClaimsPrincipal user,
        TimeProvider clock, CancellationToken ct)
    {
        if (!await CatalogEntriesExistAsync(input, db, ct))
            return Results.BadRequest(new { message = "Выберите автомобиль и конфигурацию из справочников." });
        var pair = MakePair(Guid.NewGuid(), input, user, clock);
        return await AddAsync(pair, db, ct);
    }

    private static async Task<IResult> EditPairAsync(Guid id, PairRequest input, KrylyaDb db, ClaimsPrincipal user,
        TimeProvider clock, CancellationToken ct)
    {
        var pair = await db.ChallengePairs.FindAsync([id], ct);
        if (pair is null)
            return Results.NotFound();
        if (!await CatalogEntriesExistAsync(input, db, ct))
            return Results.BadRequest(new { message = "Выберите автомобиль и конфигурацию из справочников." });
        db.Entry(pair).CurrentValues.SetValues(MakePair(id, input, user, clock));
        return await SaveAsync(pair, db, ct, created: false);
    }

    private static async Task<bool> CatalogEntriesExistAsync(PairRequest input, KrylyaDb db, CancellationToken ct) =>
        await db.Vehicles.AnyAsync(v => v.Id == input.VehicleId, ct)
        && await db.Circuits.AnyAsync(t => t.Id == input.CircuitId, ct);

    private static ChallengePair MakePair(Guid id, PairRequest input, ClaimsPrincipal user, TimeProvider clock) =>
        new(id, input.Simulator.Trim(), input.VehicleId, input.CircuitId, input.Rules.Trim(), input.Enabled,
            AuthEndpoints.Actor(user), clock.GetUtcNow());

    private static Task<IResult> AddAsync(object entry, KrylyaDb db, CancellationToken ct)
    {
        db.Add(entry);
        return SaveAsync(entry, db, ct, created: true);
    }

    private static async Task<IResult> ReplaceAsync<T>(T entry, Guid id, KrylyaDb db, CancellationToken ct) where T : class
    {
        var existing = await db.Set<T>().FindAsync([id], ct);
        if (existing is null)
            return Results.NotFound();
        db.Entry(existing).CurrentValues.SetValues(entry);
        return await SaveAsync(existing, db, ct, created: false);
    }

    private static async Task<IResult> SaveAsync(object entry, KrylyaDb db, CancellationToken ct, bool created)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { return Results.Conflict(new { message = "Такая запись уже есть в справочнике." }); }
        return created ? Results.Created(uri: (string?)null, value: entry) : Results.Ok(entry);
    }
}

public sealed record VehicleRequest([Required, StringLength(120)] string Name);
public sealed record CircuitRequest([Required, StringLength(120)] string Name, [StringLength(100)] string? Configuration);
public sealed record PairRequest([Required, StringLength(120)] string Simulator, Guid VehicleId, Guid CircuitId,
    [Required, StringLength(5000)] string Rules, bool Enabled = true);
