using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weavenest.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingToLongTermMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmbeddingJson",
                table: "LongTermMemories",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmbeddingJson",
                table: "LongTermMemories");
        }
    }
}
