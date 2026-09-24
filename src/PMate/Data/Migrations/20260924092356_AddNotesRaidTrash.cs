using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PMate.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotesRaidTrash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeleteBatchId",
                table: "Updates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Updates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeleteBatchId",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeleteBatchId",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeleteBatchId",
                table: "Milestones",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Milestones",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeleteBatches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    ItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeleteBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: true),
                    NoteDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Pinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSample = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeleteBatchId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RaidItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Owner = table.Column<string>(type: "TEXT", nullable: true),
                    Impact = table.Column<string>(type: "TEXT", nullable: false),
                    Likelihood = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Response = table.Column<string>(type: "TEXT", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsSample = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeleteBatchId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaidItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Updates_DeleteBatchId",
                table: "Updates",
                column: "DeleteBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_DeleteBatchId",
                table: "Tasks",
                column: "DeleteBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_DeleteBatchId",
                table: "Projects",
                column: "DeleteBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Milestones_DeleteBatchId",
                table: "Milestones",
                column: "DeleteBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_DeleteBatchId",
                table: "Notes",
                column: "DeleteBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_NoteDate",
                table: "Notes",
                column: "NoteDate");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_ProjectId",
                table: "Notes",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_RaidItems_DeleteBatchId",
                table: "RaidItems",
                column: "DeleteBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_RaidItems_ProjectId_Type",
                table: "RaidItems",
                columns: new[] { "ProjectId", "Type" });

            // Full-text search for notes. "trigram" matches any 3+ character substring,
            // so it works for Chinese too. The triggers keep the index in step with the table.
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE NotesFts USING fts5(Title, Body, Tags, content='Notes', content_rowid='rowid', tokenize='trigram');
                CREATE TRIGGER Notes_fts_insert AFTER INSERT ON Notes BEGIN
                  INSERT INTO NotesFts(rowid, Title, Body, Tags) VALUES (new.rowid, new.Title, new.Body, new.Tags);
                END;
                CREATE TRIGGER Notes_fts_delete AFTER DELETE ON Notes BEGIN
                  INSERT INTO NotesFts(NotesFts, rowid, Title, Body, Tags) VALUES ('delete', old.rowid, old.Title, old.Body, old.Tags);
                END;
                CREATE TRIGGER Notes_fts_update AFTER UPDATE ON Notes BEGIN
                  INSERT INTO NotesFts(NotesFts, rowid, Title, Body, Tags) VALUES ('delete', old.rowid, old.Title, old.Body, old.Tags);
                  INSERT INTO NotesFts(rowid, Title, Body, Tags) VALUES (new.rowid, new.Title, new.Body, new.Tags);
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS Notes_fts_insert;
                DROP TRIGGER IF EXISTS Notes_fts_delete;
                DROP TRIGGER IF EXISTS Notes_fts_update;
                DROP TABLE IF EXISTS NotesFts;
                """);

            migrationBuilder.DropTable(
                name: "DeleteBatches");

            migrationBuilder.DropTable(
                name: "Notes");

            migrationBuilder.DropTable(
                name: "RaidItems");

            migrationBuilder.DropIndex(
                name: "IX_Updates_DeleteBatchId",
                table: "Updates");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_DeleteBatchId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Projects_DeleteBatchId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Milestones_DeleteBatchId",
                table: "Milestones");

            migrationBuilder.DropColumn(
                name: "DeleteBatchId",
                table: "Updates");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Updates");

            migrationBuilder.DropColumn(
                name: "DeleteBatchId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "DeleteBatchId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "DeleteBatchId",
                table: "Milestones");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Milestones");
        }
    }
}
