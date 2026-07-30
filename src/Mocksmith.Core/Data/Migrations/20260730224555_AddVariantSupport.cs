using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mocksmith.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceSampleId",
                table: "DraftSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DraftSessions_SourceSampleId",
                table: "DraftSessions",
                column: "SourceSampleId");

            migrationBuilder.AddForeignKey(
                name: "FK_DraftSessions_Samples_SourceSampleId",
                table: "DraftSessions",
                column: "SourceSampleId",
                principalTable: "Samples",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DraftSessions_Samples_SourceSampleId",
                table: "DraftSessions");

            migrationBuilder.DropIndex(
                name: "IX_DraftSessions_SourceSampleId",
                table: "DraftSessions");

            migrationBuilder.DropColumn(
                name: "SourceSampleId",
                table: "DraftSessions");
        }
    }
}
