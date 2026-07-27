using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Afrobotics.Bit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddJobPauseAndStateToContentItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""ContentItems"" ADD COLUMN IF NOT EXISTS ""IsDetectionPaused"" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE ""ContentItems"" ADD COLUMN IF NOT EXISTS ""JobState"" character varying(50) NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""ContentItems"" DROP COLUMN IF EXISTS ""IsDetectionPaused"";
                ALTER TABLE ""ContentItems"" DROP COLUMN IF EXISTS ""JobState"";
            ");
        }
    }
}
