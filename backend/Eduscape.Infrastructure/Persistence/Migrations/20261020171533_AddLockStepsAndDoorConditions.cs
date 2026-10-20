using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eduscape.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Locks get steps (a jsonb list), doors and exit doors an open condition, rooms a wait after wrong answers, and
    /// teams their progress per step. Existing data moves over:
    /// - every lock becomes a lock with one step, made from its old question columns;
    /// - a door or exit door that waited for a lock (unlockedBy) opens with that lock; all others open once their
    ///   scene's locks are solved, and doors no longer wait with unlockedBy;
    /// - solved locks have their one step solved, unsolved ones keep their wrong answers for the hint.
    /// Expand/contract: the old lock columns (Question, AnswerType, CorrectAnswer, Tolerance, Hint, Options) and the
    /// TeamOpenedDoors table stay, unused, so the previous version keeps running until the new one is deployed. A later
    /// release drops them.
    /// </summary>
    public partial class AddLockStepsAndDoorConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Steps",
                table: "RoomObjects",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpenCondition",
                table: "RoomObjects",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "RequiredLockIds",
                table: "RoomObjects",
                type: "uuid[]",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WrongAnswerCooldownSeconds",
                table: "EscapeRooms",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "SolvedSteps",
                table: "TeamLockProgress",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StepWrongAttempts",
                table: "TeamLockProgress",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The JSON shape is the one LockStepsJson reads and writes.
            migrationBuilder.Sql("""
                UPDATE "RoomObjects"
                SET "Steps" = jsonb_build_array(jsonb_strip_nulls(jsonb_build_object(
                    'Id', gen_random_uuid(),
                    'Question', COALESCE("Question", ''),
                    'AnswerType', COALESCE("AnswerType", 'Code'),
                    'CorrectAnswer', COALESCE("CorrectAnswer", ''),
                    'Tolerance', "Tolerance",
                    'Hint', "Hint",
                    'Options', CASE WHEN "AnswerType" = 'MultipleChoice' THEN COALESCE(to_jsonb("Options"), '[]'::jsonb) ELSE '[]'::jsonb END,
                    'AcceptedAnswers', '[]'::jsonb,
                    'AcceptTypos', false,
                    'OrderItems', '[]'::jsonb)))
                WHERE "ObjectType" = 'Lock';

                UPDATE "RoomObjects" AS doorway
                SET "OpenCondition" = 'SelectedLocks', "RequiredLockIds" = ARRAY[doorway."UnlockedById"]
                FROM "RoomObjects" AS source
                WHERE doorway."ObjectType" IN ('Door', 'ExitDoor')
                  AND source."Id" = doorway."UnlockedById"
                  AND source."ObjectType" = 'Lock';

                UPDATE "RoomObjects"
                SET "OpenCondition" = 'AllInScene'
                WHERE "ObjectType" IN ('Door', 'ExitDoor') AND "OpenCondition" IS NULL;

                UPDATE "RoomObjects"
                SET "UnlockedById" = NULL
                WHERE "ObjectType" IN ('Door', 'ExitDoor');

                UPDATE "TeamLockProgress" SET "SolvedSteps" = 1 WHERE "SolvedAt" IS NOT NULL;
                UPDATE "TeamLockProgress" SET "StepWrongAttempts" = "WrongAttempts" WHERE "SolvedAt" IS NULL;

                -- The seeded demo room waits only a little, like DemoRoomSeeder.DemoCooldownSeconds.
                UPDATE "EscapeRooms" SET "WrongAnswerCooldownSeconds" = 5 WHERE "Id" = '01990000-0000-7000-8000-00000000de30';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old lock columns were kept, so going back only removes what was added. Doors that waited for a lock
            // are visible from the start afterwards.
            migrationBuilder.DropColumn(
                name: "Steps",
                table: "RoomObjects");

            migrationBuilder.DropColumn(
                name: "OpenCondition",
                table: "RoomObjects");

            migrationBuilder.DropColumn(
                name: "RequiredLockIds",
                table: "RoomObjects");

            migrationBuilder.DropColumn(
                name: "WrongAnswerCooldownSeconds",
                table: "EscapeRooms");

            migrationBuilder.DropColumn(
                name: "SolvedSteps",
                table: "TeamLockProgress");

            migrationBuilder.DropColumn(
                name: "StepWrongAttempts",
                table: "TeamLockProgress");
        }
    }
}
