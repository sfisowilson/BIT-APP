using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Afrobotics.Bit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSceneThumbnailAndSurfaceStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent column additions for SceneItems
            migrationBuilder.Sql("ALTER TABLE \"SceneItems\" ADD COLUMN IF NOT EXISTS \"SurfaceStatus\" character varying(20) NOT NULL DEFAULT 'Pending';");
            migrationBuilder.Sql("ALTER TABLE \"SceneItems\" ADD COLUMN IF NOT EXISTS \"ThumbnailPath\" character varying(500);");

            // Idempotent schema updates for UsageRecords
            migrationBuilder.Sql("ALTER TABLE \"UsageRecords\" DROP COLUMN IF EXISTS \"Action\";");
            migrationBuilder.Sql("ALTER TABLE \"UsageRecords\" DROP COLUMN IF EXISTS \"Endpoint\";");
            migrationBuilder.Sql("ALTER TABLE \"UsageRecords\" DROP COLUMN IF EXISTS \"ResponseTimeMs\";");
            migrationBuilder.Sql("ALTER TABLE \"UsageRecords\" DROP COLUMN IF EXISTS \"UserAgent\";");

            migrationBuilder.Sql("ALTER TABLE \"UsageRecords\" ADD COLUMN IF NOT EXISTS \"DurationMs\" bigint NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("ALTER TABLE \"UsageRecords\" ADD COLUMN IF NOT EXISTS \"HttpMethod\" character varying(10) NOT NULL DEFAULT '';");
            migrationBuilder.Sql("ALTER TABLE \"UsageRecords\" ADD COLUMN IF NOT EXISTS \"RequestPath\" character varying(500) NOT NULL DEFAULT '';");
            migrationBuilder.Sql("ALTER TABLE \"UsageRecords\" ADD COLUMN IF NOT EXISTS \"UserEmail\" character varying(200);");

            // Idempotent index creation
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_SurfaceItems_SceneId\" ON \"SurfaceItems\" (\"SceneId\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_SceneItems_ContentId\" ON \"SceneItems\" (\"ContentId\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Renders_ContentId\" ON \"Renders\" (\"ContentId\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Renders_SurfaceId\" ON \"Renders\" (\"SurfaceId\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Approvals_AdSlotId\" ON \"Approvals\" (\"AdSlotId\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_AdSlots_SurfaceId\" ON \"AdSlots\" (\"SurfaceId\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"SceneItems\" DROP COLUMN IF EXISTS \"SurfaceStatus\";");
            migrationBuilder.Sql("ALTER TABLE \"SceneItems\" DROP COLUMN IF EXISTS \"ThumbnailPath\";");
        }
    }
}
