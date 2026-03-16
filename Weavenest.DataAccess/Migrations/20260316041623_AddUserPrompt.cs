using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weavenest.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPrompt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserPrompt",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UserPrompt",
                table: "Users");
        }
    }
}
