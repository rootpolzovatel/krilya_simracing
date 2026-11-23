using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Krylya.Api.Migrations
{
    /// <inheritdoc />
    public partial class LocalOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                table: "TaskAttempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                table: "Laps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BookingId",
                table: "Attendances",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChangeEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PilotId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Details = table.Column<string>(type: "text", nullable: false),
                    Actor = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InstructorBenchmarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Instructor = table.Column<string>(type: "text", nullable: false),
                    Event = table.Column<string>(type: "text", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstructorBenchmarks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_BookingId_Date",
                table: "Attendances",
                columns: new[] { "BookingId", "Date" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Bookings_BookingId",
                table: "Attendances",
                column: "BookingId",
                principalTable: "Bookings",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attendances_Bookings_BookingId",
                table: "Attendances");

            migrationBuilder.DropTable(
                name: "ChangeEvents");

            migrationBuilder.DropTable(
                name: "InstructorBenchmarks");

            migrationBuilder.DropIndex(
                name: "IX_Attendances_BookingId_Date",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "TaskAttempts");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "Laps");

            migrationBuilder.DropColumn(
                name: "BookingId",
                table: "Attendances");
        }
    }
}
