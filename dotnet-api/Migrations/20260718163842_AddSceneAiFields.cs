using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Afrobotics.Bit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSceneAiFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiModelUsed",
                table: "SceneItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiOutputDescription",
                table: "SceneItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiPrompt",
                table: "SceneItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiStatus",
                table: "SceneItems",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiModelUsed",
                table: "SceneItems");

            migrationBuilder.DropColumn(
                name: "AiOutputDescription",
                table: "SceneItems");

            migrationBuilder.DropColumn(
                name: "AiPrompt",
                table: "SceneItems");

            migrationBuilder.DropColumn(
                name: "AiStatus",
                table: "SceneItems");
        }
    }
}
