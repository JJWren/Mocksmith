using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mocksmith.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGenerationSessionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "EstimatedCostUsd",
                table: "GenerationLogs",
                type: "TEXT",
                precision: 10,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 10,
                oldScale: 6);

            migrationBuilder.AddColumn<string>(
                name: "Backend",
                table: "GenerationLogs",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "DraftSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "DraftSessions",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "DraftSessions",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "DraftIterations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "DraftIterations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagsJson",
                table: "DraftIterations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Backend",
                table: "GenerationLogs");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "DraftSessions");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "DraftSessions");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "DraftSessions");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "DraftIterations");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "DraftIterations");

            migrationBuilder.DropColumn(
                name: "TagsJson",
                table: "DraftIterations");

            migrationBuilder.AlterColumn<decimal>(
                name: "EstimatedCostUsd",
                table: "GenerationLogs",
                type: "TEXT",
                precision: 10,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 10,
                oldScale: 6,
                oldNullable: true);
        }
    }
}
