using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Krylya.Api.Migrations
{
    /// <inheritdoc />
    public partial class MonthlyInstructorLap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InstructorLapMilliseconds",
                table: "Challenges",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstructorLapMilliseconds",
                table: "Challenges");
        }
    }
}
