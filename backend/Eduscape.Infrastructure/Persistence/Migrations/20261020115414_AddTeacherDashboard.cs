using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eduscape.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeacherDashboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParticipantCount",
                table: "Teams",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstAttemptAt",
                table: "TeamLockProgress",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAttemptAt",
                table: "TeamLockProgress",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentSceneId",
                table: "Participants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SceneEnteredAt",
                table: "Participants",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParticipantCount",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "FirstAttemptAt",
                table: "TeamLockProgress");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "TeamLockProgress");

            migrationBuilder.DropColumn(
                name: "CurrentSceneId",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "SceneEnteredAt",
                table: "Participants");
        }
    }
}
