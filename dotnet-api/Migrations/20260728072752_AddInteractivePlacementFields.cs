using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Afrobotics.Bit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInteractivePlacementFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssetType",
                table: "SurfaceItems",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "SurfaceItems",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TrackingDataJson",
                table: "SurfaceItems",
                type: "character varying(100000)",
                maxLength: 100000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompositingEngine",
                table: "Renders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "QualityTier",
                table: "Renders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssetType",
                table: "SurfaceItems");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "SurfaceItems");

            migrationBuilder.DropColumn(
                name: "TrackingDataJson",
                table: "SurfaceItems");

            migrationBuilder.DropColumn(
                name: "CompositingEngine",
                table: "Renders");

            migrationBuilder.DropColumn(
                name: "QualityTier",
                table: "Renders");
        }
    }
}
