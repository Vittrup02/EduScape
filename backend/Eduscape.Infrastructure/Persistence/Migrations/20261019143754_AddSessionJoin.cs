using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eduscape.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionJoin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DeviceToken",
                table: "Participants",
                newName: "DeviceTokenHash");

            migrationBuilder.RenameIndex(
                name: "IX_Participants_DeviceToken",
                table: "Participants",
                newName: "IX_Participants_DeviceTokenHash");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "Sessions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Sessions started before PINs expired get the default lifetime.
            migrationBuilder.Sql("""UPDATE "Sessions" SET "ExpiresAt" = "StartedAt" + interval '12 hours';""");

            migrationBuilder.AlterColumn<string>(
                name: "Nickname",
                table: "Participants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Status_ExpiresAt",
                table: "Sessions",
                columns: new[] { "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_Status_ExpiresAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Sessions");

            migrationBuilder.RenameColumn(
                name: "DeviceTokenHash",
                table: "Participants",
                newName: "DeviceToken");

            migrationBuilder.RenameIndex(
                name: "IX_Participants_DeviceTokenHash",
                table: "Participants",
                newName: "IX_Participants_DeviceToken");

            migrationBuilder.AlterColumn<string>(
                name: "Nickname",
                table: "Participants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }
    }
}
