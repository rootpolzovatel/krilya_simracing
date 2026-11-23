using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Krylya.Api.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionAndTraining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PointsPrecision",
                table: "Tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedBy",
                table: "Tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PointsPrecision",
                table: "TaskAttempts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ResultMilliseconds",
                table: "TaskAttempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "TaskAttempts"
                SET "ResultMilliseconds" = split_part("Result", ':', 1)::bigint * 60000
                    + split_part(split_part("Result", ':', 2), '.', 1)::bigint * 1000
                    + split_part("Result", '.', 2)::bigint
                WHERE "ResultType" = 'time' AND "Result" ~ '^(0|[1-9][0-9]{0,4}):[0-5][0-9]\.[0-9]{3}$'
                """);

            migrationBuilder.AddColumn<int>(
                name: "MinimumCompletedStage",
                table: "RankRules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "RankRules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedBy",
                table: "RankRules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublicationChangedAt",
                table: "Pilots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublicationChangedBy",
                table: "Pilots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FinalizedAt",
                table: "Challenges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourcePairId",
                table: "Challenges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Challenges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedBy",
                table: "Challenges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SessionVersion",
                table: "Accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ChallengeStandings",
                columns: table => new
                {
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PilotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Place = table.Column<int>(type: "integer", nullable: false),
                    Milliseconds = table.Column<int>(type: "integer", nullable: false),
                    GapMilliseconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChallengeStandings", x => new { x.ChallengeId, x.PilotId });
                    table.ForeignKey(
                        name: "FK_ChallengeStandings_Challenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "Challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChallengeStandings_Pilots_PilotId",
                        column: x => x.PilotId,
                        principalTable: "Pilots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Circuits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Configuration = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Circuits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskUnlocks",
                columns: table => new
                {
                    PilotId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskUnlocks", x => new { x.PilotId, x.TaskId });
                    table.ForeignKey(
                        name: "FK_TaskUnlocks_Pilots_PilotId",
                        column: x => x.PilotId,
                        principalTable: "Pilots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskUnlocks_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChallengePairs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Simulator = table.Column<string>(type: "text", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CircuitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rules = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChallengePairs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChallengePairs_Circuits_CircuitId",
                        column: x => x.CircuitId,
                        principalTable: "Circuits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChallengePairs_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChallengePairs_CircuitId",
                table: "ChallengePairs",
                column: "CircuitId");

            migrationBuilder.CreateIndex(
                name: "IX_ChallengePairs_Simulator_VehicleId_CircuitId",
                table: "ChallengePairs",
                columns: new[] { "Simulator", "VehicleId", "CircuitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChallengePairs_VehicleId",
                table: "ChallengePairs",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeStandings_PilotId",
                table: "ChallengeStandings",
                column: "PilotId");

            migrationBuilder.CreateIndex(
                name: "IX_Circuits_Name_Configuration",
                table: "Circuits",
                columns: new[] { "Name", "Configuration" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskUnlocks_TaskId",
                table: "TaskUnlocks",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_Name",
                table: "Vehicles",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChallengePairs");

            migrationBuilder.DropTable(
                name: "ChallengeStandings");

            migrationBuilder.DropTable(
                name: "TaskUnlocks");

            migrationBuilder.DropTable(
                name: "Circuits");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropColumn(
                name: "PointsPrecision",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "PointsPrecision",
                table: "TaskAttempts");

            migrationBuilder.DropColumn(
                name: "ResultMilliseconds",
                table: "TaskAttempts");

            migrationBuilder.DropColumn(
                name: "MinimumCompletedStage",
                table: "RankRules");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "RankRules");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "RankRules");

            migrationBuilder.DropColumn(
                name: "PublicationChangedAt",
                table: "Pilots");

            migrationBuilder.DropColumn(
                name: "PublicationChangedBy",
                table: "Pilots");

            migrationBuilder.DropColumn(
                name: "FinalizedAt",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "SourcePairId",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "SessionVersion",
                table: "Accounts");
        }
    }
}
