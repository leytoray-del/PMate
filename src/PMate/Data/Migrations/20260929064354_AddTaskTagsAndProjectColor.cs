using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PMate.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskTagsAndProjectColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TagId",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "Projects",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            // Default tags, added once. Admins can change them later, and a restart does not bring back removed ones.
            // Literal values: a migration must not change when TagService.Defaults() changes.
            migrationBuilder.InsertData(
                table: "Tags",
                columns: ["Id", "Name", "Color", "SortOrder"],
                values: new object[,]
                {
                    { "meeting", "Meeting", "blue", 0 },
                    { "onsite", "On-site", "orange", 1 },
                    { "doc", "Doc", "indigo", 2 },
                    { "review", "Review", "purple", 3 },
                    { "confirm", "Confirm", "teal", 4 },
                    { "followup", "Follow-up", "amber", 5 },
                    { "approve", "Approve", "pink", 6 },
                    { "report", "Report", "green", 7 },
                    { "fix", "Fix", "red", 8 },
                    { "test", "Test", "gray", 9 },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropColumn(
                name: "TagId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "Projects");
        }
    }
}
