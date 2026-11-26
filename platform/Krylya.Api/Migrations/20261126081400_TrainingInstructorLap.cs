using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Krylya.Api.Migrations
{
    /// <inheritdoc />
    public partial class TrainingInstructorLap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "InstructorLapMilliseconds",
                table: "Tasks",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstructorLapMilliseconds",
                table: "Tasks");
        }
    }
}
