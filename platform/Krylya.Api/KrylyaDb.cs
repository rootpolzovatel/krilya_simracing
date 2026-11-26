using Microsoft.EntityFrameworkCore;

namespace Krylya.Api;

public sealed class KrylyaDb(DbContextOptions<KrylyaDb> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Pilot> Pilots => Set<Pilot>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingSkip> BookingSkips => Set<BookingSkip>();
    public DbSet<Attendance> Attendances => Set<Attendance>();
    public DbSet<TrainingTask> Tasks => Set<TrainingTask>();
    public DbSet<TaskAttempt> TaskAttempts => Set<TaskAttempt>();
    public DbSet<RankRule> RankRules => Set<RankRule>();
    public DbSet<Challenge> Challenges => Set<Challenge>();
    public DbSet<ChallengeImage> ChallengeImages => Set<ChallengeImage>();
    public DbSet<Lap> Laps => Set<Lap>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Circuit> Circuits => Set<Circuit>();
    public DbSet<ChallengePair> ChallengePairs => Set<ChallengePair>();
    public DbSet<ChallengeStanding> ChallengeStandings => Set<ChallengeStanding>();
    public DbSet<TaskUnlock> TaskUnlocks => Set<TaskUnlock>();
    public DbSet<TrainingStage> TrainingStages => Set<TrainingStage>();

    public DbSet<ChangeEvent> ChangeEvents => Set<ChangeEvent>();
    public DbSet<InstructorBenchmark> InstructorBenchmarks => Set<InstructorBenchmark>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Lap>().HasQueryFilter(l => l.VoidedAt == null);
        model.Entity<TaskAttempt>().HasQueryFilter(a => a.VoidedAt == null);
        model.Entity<Attendance>().HasIndex(a => new { a.BookingId, a.Date }).IsUnique();
        model.Entity<Attendance>().HasOne<Booking>().WithMany().HasForeignKey(a => a.BookingId);
        model.Entity<Account>().HasIndex(a => a.Login).IsUnique();
        model.Entity<Pilot>().HasOne<Account>().WithOne().HasForeignKey<Pilot>(p => p.Id);
        model.Entity<Booking>().HasOne<Pilot>().WithMany().HasForeignKey(b => b.PilotId);
        model.Entity<Booking>().HasIndex(b => new { b.Weekday, b.StartHour });
        model.Entity<BookingSkip>().HasKey(s => new { s.BookingId, s.Date });
        model.Entity<BookingSkip>().HasOne<Booking>().WithMany(b => b.Skips).HasForeignKey(s => s.BookingId);
        model.Entity<Attendance>().HasOne<Pilot>().WithMany().HasForeignKey(a => a.PilotId);
        model.Entity<TaskAttempt>().HasOne<Pilot>().WithMany().HasForeignKey(a => a.PilotId);
        model.Entity<TaskAttempt>().HasOne<TrainingTask>().WithMany().HasForeignKey(a => a.TaskId);
        model.Entity<TaskUnlock>().HasKey(u => new { u.PilotId, u.TaskId });
        model.Entity<TaskUnlock>().HasOne<Pilot>().WithMany().HasForeignKey(u => u.PilotId);
        model.Entity<TaskUnlock>().HasOne<TrainingTask>().WithMany().HasForeignKey(u => u.TaskId);
        model.Entity<TrainingTask>().HasIndex(t => new { t.Stage, t.Order }).IsUnique();
        model.Entity<TrainingStage>().HasKey(s => s.Number);
        model.Entity<TrainingStage>().Property(s => s.Number).ValueGeneratedNever();
        model.Entity<RankRule>().HasIndex(r => r.Order).IsUnique();
        model.Entity<Challenge>().HasIndex(c => c.Month).IsUnique();
        model.Entity<ChallengeImage>().HasKey(i => new { i.ChallengeId, i.Kind });
        model.Entity<ChallengeImage>().HasOne<Challenge>().WithMany().HasForeignKey(i => i.ChallengeId);
        model.Entity<Lap>().HasOne<Challenge>().WithMany().HasForeignKey(l => l.ChallengeId);
        model.Entity<Lap>().HasOne<Pilot>().WithMany().HasForeignKey(l => l.PilotId);
        model.Entity<Lap>().HasIndex(l => new { l.ChallengeId, l.PilotId, l.Milliseconds });
        model.Entity<Vehicle>().HasIndex(v => v.Name).IsUnique();
        model.Entity<Circuit>().HasIndex(t => new { t.Name, t.Configuration }).IsUnique();
        model.Entity<ChallengePair>().HasOne<Vehicle>().WithMany().HasForeignKey(p => p.VehicleId);
        model.Entity<ChallengePair>().HasOne<Circuit>().WithMany().HasForeignKey(p => p.CircuitId);
        model.Entity<ChallengePair>().HasIndex(p => new { p.Simulator, p.VehicleId, p.CircuitId }).IsUnique();
        model.Entity<ChallengeStanding>().HasKey(s => new { s.ChallengeId, s.PilotId });
        model.Entity<ChallengeStanding>().HasOne<Pilot>().WithMany().HasForeignKey(s => s.PilotId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ChallengeStanding>().HasOne<Challenge>().WithMany().HasForeignKey(s => s.ChallengeId);
    }
}

public static class Roles
{
    public const string Coach = "coach";
    public const string Admin = "admin";
    public const string Pilot = "pilot";
}

public sealed class Account
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Login { get; init; }
    public string PasswordHash { get; set; } = "";
    public required string Role { get; init; }
    public bool Active { get; set; } = true;
    public int SessionVersion { get; set; }
}

public sealed class Pilot
{
    public Guid Id { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public int OpeningPracticeMinutes { get; set; }
    public bool PublishName { get; set; }
    public Guid? PublicationChangedBy { get; set; }
    public DateTimeOffset? PublicationChangedAt { get; set; }
}

public sealed class Booking
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PilotId { get; init; }
    public DateOnly StartsOn { get; init; }
    public int Weekday { get; init; }
    public int StartHour { get; init; }
    public DateOnly? EndsBefore { get; set; }
    public Guid CreatedBy { get; init; }
    public List<BookingSkip> Skips { get; } = [];
}

public sealed record BookingSkip(Guid BookingId, DateOnly Date, Guid CreatedBy);
public sealed record Attendance(Guid Id, Guid PilotId, DateOnly Date, int Minutes, Guid CreatedBy, DateTimeOffset CreatedAt)
{
    public Guid? BookingId { get; init; }
}

public sealed record ChangeEvent(Guid Id, Guid? PilotId, string Kind, Guid SubjectId, string Details, Guid Actor, DateTimeOffset CreatedAt);
public sealed record InstructorBenchmark(Guid Id, DateOnly Month, string Instructor, string Event, string Unit, int Value);

public sealed record TrainingTask(Guid Id, int Stage, int Order, string Title, string Instructions, string ResultType,
    string Normative, int? PointsPrecision = null)
{
    public long? InstructorLapMilliseconds { get; init; }
    public Guid? UpdatedBy { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
public sealed record TrainingStage(int Number, string Title, Guid UpdatedBy, DateTimeOffset UpdatedAt);

public sealed class TaskAttempt
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PilotId { get; init; }
    public Guid TaskId { get; init; }
    public required string Result { get; init; }
    public long? ResultMilliseconds { get; init; }
    public int? PointsPrecision { get; init; }
    public required string Title { get; init; }
    public required string Instructions { get; init; }
    public required string ResultType { get; init; }
    public required string Normative { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid? ConfirmedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public bool? Passed { get; set; }
}

public sealed record TaskUnlock(Guid PilotId, Guid TaskId, Guid CreatedBy, DateTimeOffset CreatedAt);

public sealed record RankRule(Guid Id, int Order, string Title, int MinimumCompletedTasks, Guid[] RequiredTaskIds,
    int? MinimumCompletedStage = null)
{
    public Guid? UpdatedBy { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed record Challenge(Guid Id, DateOnly Month, string Simulator, string Car, string Track, string Rules)
{
    public int? InstructorLapMilliseconds { get; set; }
    public Guid? SourcePairId { get; init; }
    public DateTimeOffset? FinalizedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
public sealed record ChallengeImage(Guid ChallengeId, string Kind, byte[] Png, Guid UpdatedBy, DateTimeOffset UpdatedAt);
public sealed record Lap(Guid Id, Guid ChallengeId, Guid PilotId, int Milliseconds, Guid CreatedBy, DateTimeOffset CreatedAt)
{
    public DateTimeOffset? VoidedAt { get; set; }
}
public sealed record Vehicle(Guid Id, string Name);
public sealed record Circuit(Guid Id, string Name, string Configuration)
{
    public string Label => string.IsNullOrWhiteSpace(Configuration) ? Name : $"{Name} {Configuration}";
}
public sealed record ChallengePair(Guid Id, string Simulator, Guid VehicleId, Guid CircuitId, string Rules,
    bool Enabled, Guid UpdatedBy, DateTimeOffset UpdatedAt);
public sealed record ChallengeStanding(Guid ChallengeId, Guid PilotId, int Place, int Milliseconds, int GapMilliseconds);
