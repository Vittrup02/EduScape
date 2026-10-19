using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eduscape.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Classes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerTeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Classes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EscapeRooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GradeLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsTemplate = table.Column<bool>(type: "boolean", nullable: false),
                    OwnerTeacherId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EscapeRooms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Scenes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EscapeRoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BackgroundImageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsStartScene = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scenes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scenes_EscapeRooms_EscapeRoomId",
                        column: x => x.EscapeRoomId,
                        principalTable: "EscapeRooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EscapeRoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    Pin = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: false),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Classes_ClassId",
                        column: x => x.ClassId,
                        principalTable: "Classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sessions_EscapeRooms_EscapeRoomId",
                        column: x => x.EscapeRoomId,
                        principalTable: "EscapeRooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoomObjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SceneId = table.Column<Guid>(type: "uuid", nullable: false),
                    GraphicKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    UnlockedGraphicKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UnlockedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ObjectType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PositionX = table.Column<double>(type: "double precision", nullable: false),
                    PositionY = table.Column<double>(type: "double precision", nullable: false),
                    TargetSceneId = table.Column<Guid>(type: "uuid", nullable: true),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Question = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AnswerType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CorrectAnswer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Tolerance = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Hint = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Options = table.Column<string[]>(type: "text[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomObjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoomObjects_RoomObjects_UnlockedById",
                        column: x => x.UnlockedById,
                        principalTable: "RoomObjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RoomObjects_Scenes_SceneId",
                        column: x => x.SceneId,
                        principalTable: "Scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoomObjects_Scenes_TargetSceneId",
                        column: x => x.TargetSceneId,
                        principalTable: "Scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EscapedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Participants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nickname = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DeviceToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Participants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Participants_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamLockProgress",
                columns: table => new
                {
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockId = table.Column<Guid>(type: "uuid", nullable: false),
                    WrongAttempts = table.Column<int>(type: "integer", nullable: false),
                    SolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamLockProgress", x => new { x.TeamId, x.LockId });
                    table.ForeignKey(
                        name: "FK_TeamLockProgress_RoomObjects_LockId",
                        column: x => x.LockId,
                        principalTable: "RoomObjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamLockProgress_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamOpenedDoors",
                columns: table => new
                {
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamOpenedDoors", x => new { x.TeamId, x.DoorId });
                    table.ForeignKey(
                        name: "FK_TeamOpenedDoors_RoomObjects_DoorId",
                        column: x => x.DoorId,
                        principalTable: "RoomObjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamOpenedDoors_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Classes_OwnerTeacherId",
                table: "Classes",
                column: "OwnerTeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_EscapeRooms_OwnerTeacherId",
                table: "EscapeRooms",
                column: "OwnerTeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_EscapeRooms_Subject_GradeLevel",
                table: "EscapeRooms",
                columns: new[] { "Subject", "GradeLevel" });

            migrationBuilder.CreateIndex(
                name: "IX_Participants_DeviceToken",
                table: "Participants",
                column: "DeviceToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Participants_TeamId",
                table: "Participants",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomObjects_SceneId",
                table: "RoomObjects",
                column: "SceneId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomObjects_TargetSceneId",
                table: "RoomObjects",
                column: "TargetSceneId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomObjects_UnlockedById",
                table: "RoomObjects",
                column: "UnlockedById");

            migrationBuilder.CreateIndex(
                name: "IX_Scenes_EscapeRoomId_StartScene",
                table: "Scenes",
                column: "EscapeRoomId",
                unique: true,
                filter: "\"IsStartScene\"");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ClassId",
                table: "Sessions",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_EscapeRoomId",
                table: "Sessions",
                column: "EscapeRoomId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Pin_Active",
                table: "Sessions",
                column: "Pin",
                unique: true,
                filter: "\"Status\" <> 'Closed'");

            migrationBuilder.CreateIndex(
                name: "IX_TeamLockProgress_LockId",
                table: "TeamLockProgress",
                column: "LockId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamOpenedDoors_DoorId",
                table: "TeamOpenedDoors",
                column: "DoorId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_SessionId",
                table: "Teams",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Participants");

            migrationBuilder.DropTable(
                name: "TeamLockProgress");

            migrationBuilder.DropTable(
                name: "TeamOpenedDoors");

            migrationBuilder.DropTable(
                name: "RoomObjects");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Scenes");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "Classes");

            migrationBuilder.DropTable(
                name: "EscapeRooms");
        }
    }
}
