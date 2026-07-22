using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Afrobotics.Bit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineStageTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastErrorAt",
                table: "ContentItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorMessage",
                table: "ContentItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SceneDetectingCompletedAt",
                table: "ContentItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SceneDetectingStartedAt",
                table: "ContentItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StagingCompletedAt",
                table: "ContentItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TranscodingCompletedAt",
                table: "ContentItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TranscodingStartedAt",
                table: "ContentItems",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastErrorAt",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "LastErrorMessage",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "SceneDetectingCompletedAt",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "SceneDetectingStartedAt",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "StagingCompletedAt",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "TranscodingCompletedAt",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "TranscodingStartedAt",
                table: "ContentItems");
        }
    }
}
