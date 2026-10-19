using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eduscape.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEditorAndPreview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ClassId",
                table: "Sessions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<bool>(
                name: "IsPreview",
                table: "Sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "EscapeRooms",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sessions_ClassUnlessPreview",
                table: "Sessions",
                sql: "\"IsPreview\" OR \"ClassId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Sessions_ClassUnlessPreview",
                table: "Sessions");

            // Previews have no class, so they cannot survive ClassId becoming required again.
            migrationBuilder.Sql("DELETE FROM \"Sessions\" WHERE \"ClassId\" IS NULL;");

            migrationBuilder.DropColumn(
                name: "IsPreview",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "EscapeRooms");

            migrationBuilder.AlterColumn<Guid>(
                name: "ClassId",
                table: "Sessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
