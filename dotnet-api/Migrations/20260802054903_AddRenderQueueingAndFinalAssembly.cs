using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Afrobotics.Bit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRenderQueueingAndFinalAssembly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsQueuedForFinal",
                table: "Renders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SceneClipStorageKey",
                table: "Renders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalAssemblyErrorMessage",
                table: "ContentItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FinalAssemblyProgress",
                table: "ContentItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FinalAssemblyStatus",
                table: "ContentItems",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalAssemblyUpdatedAt",
                table: "ContentItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalVideoStorageKey",
                table: "ContentItems",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsQueuedForFinal",
                table: "Renders");

            migrationBuilder.DropColumn(
                name: "SceneClipStorageKey",
                table: "Renders");

            migrationBuilder.DropColumn(
                name: "FinalAssemblyErrorMessage",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "FinalAssemblyProgress",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "FinalAssemblyStatus",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "FinalAssemblyUpdatedAt",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "FinalVideoStorageKey",
                table: "ContentItems");
        }
    }
}
