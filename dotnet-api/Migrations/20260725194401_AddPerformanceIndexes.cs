using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Afrobotics.Bit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SurfaceItems_Status",
                table: "SurfaceItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Renders_CampaignId",
                table: "Renders",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_CampaignId",
                table: "ContentItems",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_DetectionJobId",
                table: "ContentItems",
                column: "DetectionJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_IngestionStatus",
                table: "ContentItems",
                column: "IngestionStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_Status",
                table: "Campaigns",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SurfaceItems_Status",
                table: "SurfaceItems");

            migrationBuilder.DropIndex(
                name: "IX_Renders_CampaignId",
                table: "Renders");

            migrationBuilder.DropIndex(
                name: "IX_ContentItems_CampaignId",
                table: "ContentItems");

            migrationBuilder.DropIndex(
                name: "IX_ContentItems_DetectionJobId",
                table: "ContentItems");

            migrationBuilder.DropIndex(
                name: "IX_ContentItems_IngestionStatus",
                table: "ContentItems");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_Status",
                table: "Campaigns");
        }
    }
}
