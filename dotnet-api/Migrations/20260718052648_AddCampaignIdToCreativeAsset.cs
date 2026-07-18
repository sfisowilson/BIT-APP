using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Afrobotics.Bit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignIdToCreativeAsset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CampaignId",
                table: "CreativeAssets",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreativeAssets_CampaignId",
                table: "CreativeAssets",
                column: "CampaignId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreativeAssets_Campaigns_CampaignId",
                table: "CreativeAssets",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreativeAssets_Campaigns_CampaignId",
                table: "CreativeAssets");

            migrationBuilder.DropIndex(
                name: "IX_CreativeAssets_CampaignId",
                table: "CreativeAssets");

            migrationBuilder.DropColumn(
                name: "CampaignId",
                table: "CreativeAssets");
        }
    }
}
