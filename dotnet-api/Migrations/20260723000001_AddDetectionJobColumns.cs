using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Afrobotics.Bit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDetectionJobColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DetectionProgress",
                table: "ContentItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DetectionJobId",
                table: "ContentItems",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetectionJobId",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "DetectionProgress",
                table: "ContentItems");
        }
    }
}
