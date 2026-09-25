using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PMate.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Content",
                table: "Notes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Notes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Content",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Notes");
        }
    }
}
