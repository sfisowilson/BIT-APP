using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Afrobotics.Bit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptPlacementFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "SurfaceId",
                table: "Renders",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "PreviewStorageKey",
                table: "Renders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromptText",
                table: "Renders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenderMode",
                table: "Renders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SceneId",
                table: "Renders",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Renders_SceneId",
                table: "Renders",
                column: "SceneId");

            migrationBuilder.AddForeignKey(
                name: "FK_Renders_SceneItems_SceneId",
                table: "Renders",
                column: "SceneId",
                principalTable: "SceneItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Renders_SceneItems_SceneId",
                table: "Renders");

            migrationBuilder.DropIndex(
                name: "IX_Renders_SceneId",
                table: "Renders");

            migrationBuilder.DropColumn(
                name: "PreviewStorageKey",
                table: "Renders");

            migrationBuilder.DropColumn(
                name: "PromptText",
                table: "Renders");

            migrationBuilder.DropColumn(
                name: "RenderMode",
                table: "Renders");

            migrationBuilder.DropColumn(
                name: "SceneId",
                table: "Renders");

            migrationBuilder.AlterColumn<string>(
                name: "SurfaceId",
                table: "Renders",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
