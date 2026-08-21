using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Afrobotics.Bit.Api.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Data-only migration, no schema change. SceneItems.DurationSeconds was computed as
    /// (EndFrame - StartFrame) / fps in ShotDetectionPipeline.cs / ShotClusteringService.cs /
    /// SurfaceDetectionPipeline.cs, but EndFrame is inclusive, so every scene's stored duration is
    /// under-counted by one frame -- and that field feeds the invoice's "placement exposure
    /// duration" calculation (governance/features/full-video-pipeline.gherkin) directly. The
    /// computation itself was fixed in a prior migration; this backfills every row that already
    /// existed before that fix, using each scene's own video's real FrameRate rather than assuming
    /// a fixed fps.
    /// </remarks>
    public partial class BackfillSceneDurationSeconds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""SceneItems"" s
                SET ""DurationSeconds"" = (s.""EndFrame"" - s.""StartFrame"" + 1)::double precision / GREATEST(c.""FrameRate"", 1)
                FROM ""ContentItems"" c
                WHERE c.""Id"" = s.""ContentId"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible — the pre-backfill (under-counted) values aren't recoverable from
            // EndFrame/StartFrame/FrameRate alone, since that's exactly what produced the wrong
            // values in the first place.
        }
    }
}
