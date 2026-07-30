using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mocksmith.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHandoffFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BriefMarkdown",
                table: "Samples",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BriefMarkdown",
                table: "Samples");
        }
    }
}
