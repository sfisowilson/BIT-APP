using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Afrobotics.Bit.Api.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// SurfaceItem.DetectedAtFrame (dotnet-api/Models/Models.cs) was already present in
    /// PostgresDbContextModelSnapshot.cs, but no prior migration actually added the column —
    /// it silently never made it into any generated migration, so every database migrated from
    /// scratch (and any deploy that only trusts __EFMigrationsHistory) is missing it. This
    /// migration exists purely to close that gap; it does not change the target model at all.
    /// </remarks>
    public partial class AddDetectedAtFrameToSurfaceItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DetectedAtFrame",
                table: "SurfaceItems",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetectedAtFrame",
                table: "SurfaceItems");
        }
    }
}
