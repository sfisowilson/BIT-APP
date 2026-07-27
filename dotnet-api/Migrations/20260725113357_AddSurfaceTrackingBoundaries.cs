using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Afrobotics.Bit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSurfaceTrackingBoundaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TrackedBoundariesJson on SurfaceItems — use raw SQL with IF NOT EXISTS
            // because the EF migration transaction previously rolled back this column
            // when IsDetectionPaused/JobState (already added by earlier migration) failed.
            migrationBuilder.Sql(
                @"ALTER TABLE ""SurfaceItems"" ADD COLUMN IF NOT EXISTS ""TrackedBoundariesJson"" character varying(100000) NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"ALTER TABLE ""SurfaceItems"" DROP COLUMN IF EXISTS ""TrackedBoundariesJson"";");
        }
    }
}
