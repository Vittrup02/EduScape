using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eduscape.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSceneNarration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Narration",
                table: "Scenes",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Narration",
                table: "Scenes");
        }
    }
}
