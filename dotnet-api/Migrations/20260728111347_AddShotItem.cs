using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Afrobotics.Bit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddShotItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Shots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ContentId = table.Column<string>(type: "text", nullable: false),
                    SceneId = table.Column<string>(type: "text", nullable: true),
                    ShotIndex = table.Column<int>(type: "integer", nullable: false),
                    StartFrame = table.Column<int>(type: "integer", nullable: false),
                    EndFrame = table.Column<int>(type: "integer", nullable: false),
                    KeyframeTimestamp = table.Column<double>(type: "double precision", nullable: false),
                    KeyframePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    KeyframeEmbeddingJson = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shots_SceneItems_SceneId",
                        column: x => x.SceneId,
                        principalTable: "SceneItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Shots_ContentId",
                table: "Shots",
                column: "ContentId");

            migrationBuilder.CreateIndex(
                name: "IX_Shots_SceneId",
                table: "Shots",
                column: "SceneId");

            migrationBuilder.CreateIndex(
                name: "IX_Shots_ShotIndex",
                table: "Shots",
                column: "ShotIndex");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Shots");
        }
    }
}
