using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weavenest.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmotionalStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Happiness = table.Column<float>(type: "real", nullable: false),
                    Sadness = table.Column<float>(type: "real", nullable: false),
                    Disgust = table.Column<float>(type: "real", nullable: false),
                    Fear = table.Column<float>(type: "real", nullable: false),
                    Surprise = table.Column<float>(type: "real", nullable: false),
                    Anger = table.Column<float>(type: "real", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmotionalStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HumanMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Processed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HumanMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LongTermMemories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TagsJson = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    Importance = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                    Confidence = table.Column<float>(type: "real", nullable: false, defaultValue: 0.5f),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastAccessedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LinkedMemoryIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    EmotionalContextJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSuperseded = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LongTermMemories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TickLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubconsciousContent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConsciousContent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SpokeContent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmotionalStateBeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmotionalStateAfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ToolCallsJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TickLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmotionalStates_Timestamp",
                table: "EmotionalStates",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_HumanMessages_Processed",
                table: "HumanMessages",
                column: "Processed");

            migrationBuilder.CreateIndex(
                name: "IX_HumanMessages_Timestamp",
                table: "HumanMessages",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_LongTermMemories_Category",
                table: "LongTermMemories",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_LongTermMemories_IsSuperseded",
                table: "LongTermMemories",
                column: "IsSuperseded");

            migrationBuilder.CreateIndex(
                name: "IX_LongTermMemories_LastAccessedAt",
                table: "LongTermMemories",
                column: "LastAccessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TickLogs_Timestamp",
                table: "TickLogs",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmotionalStates");

            migrationBuilder.DropTable(
                name: "HumanMessages");

            migrationBuilder.DropTable(
                name: "LongTermMemories");

            migrationBuilder.DropTable(
                name: "TickLogs");
        }
    }
}
