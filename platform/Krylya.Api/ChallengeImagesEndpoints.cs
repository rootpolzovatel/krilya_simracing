using System.Buffers.Binary;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StbImageSharp;

namespace Krylya.Api;

internal static class ChallengeImagesEndpoints
{
    private const int MaxBytes = 8 * 1024 * 1024;

    public static void MapChallengeImages(WebApplication app, RouteGroupBuilder admin)
    {
        var upload = admin.MapPut("/challenges/{id:guid}/images/{kind}", UploadAsync);
        upload.WithMetadata(new RequestSizeLimitAttribute(MaxBytes));
        app.MapGet("/api/public/challenges/{id:guid}/images/{kind}", GetAsync);
    }

    private static async Task<IResult> UploadAsync(Guid id, string kind, HttpRequest request, KrylyaDb db,
        TimeProvider clock, ClaimsPrincipal user, CancellationToken ct)
    {
        if (kind is not ("car" or "track"))
            return Results.NotFound();
        if (request.ContentLength > MaxBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        var challenge = await db.Challenges.FindAsync([id], ct);
        if (challenge is null)
            return Results.NotFound();
        if (challenge.Month < CompetitionEndpoints.MonthAt(clock.GetUtcNow()))
            return Results.Conflict(new { message = "Изображения завершённого испытания уже зафиксированы." });

        byte[] buffer = new byte[81920];
        using var stream = new MemoryStream();
        int read;
        while ((read = await request.Body.ReadAsync(buffer, ct)) != 0)
        {
            if (stream.Length + read > MaxBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            await stream.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        var png = stream.ToArray();
        if (!IsPng(png))
            return Results.BadRequest(new { message = "Загрузите действительный PNG размером до 4096 × 4096 пикселей." });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await MonthlyCompetition.LockAsync(db, ct);
        var subject = kind == "car" ? challenge.Car : challenge.Track;
        await db.Entry(challenge).ReloadAsync(ct);
        if (subject != (kind == "car" ? challenge.Car : challenge.Track))
            return Results.Conflict(new { message = "Условия испытания изменились. Откройте форму заново." });
        var now = clock.GetUtcNow();
        if (challenge.Month < CompetitionEndpoints.MonthAt(now))
            return Results.Conflict(new { message = "Испытание закрылось во время загрузки." });

        var actor = AuthEndpoints.Actor(user);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ChallengeImages" ("ChallengeId", "Kind", "Png", "UpdatedBy", "UpdatedAt")
            VALUES ({id}, {kind}, {png}, {actor}, {now})
            ON CONFLICT ("ChallengeId", "Kind") DO UPDATE
            SET "Png" = EXCLUDED."Png", "UpdatedBy" = EXCLUDED."UpdatedBy", "UpdatedAt" = EXCLUDED."UpdatedAt"
            """, ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static bool IsPng(byte[] data)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length < 33 || !data.AsSpan().StartsWith(signature) || !data.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            return false;
        var width = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20, 4));
        if (width is 0 or > 4096 || height is 0 or > 4096)
            return false;
        try
        {
            var image = ImageResult.FromMemory(data, ColorComponents.RedGreenBlueAlpha);
            return image.Width == width && image.Height == height;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<IResult> GetAsync(Guid id, string kind, ClaimsPrincipal user,
        KrylyaDb db, TimeProvider clock, CancellationToken ct)
    {
        if (kind is not ("car" or "track"))
            return Results.NotFound();
        var month = CompetitionEndpoints.MonthAt(clock.GetUtcNow());
        var admin = user.IsInRole(Roles.Admin);
        var data = await (from image in db.ChallengeImages
                          join challenge in db.Challenges on image.ChallengeId equals challenge.Id
                          where image.ChallengeId == id && image.Kind == kind && (challenge.Month <= month || admin)
                          select image.Png).SingleOrDefaultAsync(ct);
        return data is null ? Results.NotFound() : Results.File(data, "image/png");
    }
}
