using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PMate.Data.Migrations
{
    /// <summary>
    /// One-time repair. When creator ids were added, every older record went to the first admin,
    /// even records another user had made. Older records still hold the creator's display name (CreatedBy),
    /// so records that went to the first admin but name another user go back to that user.
    /// This fixes other users' hours on the timesheet and their private notes and tasks.
    /// </summary>
    public partial class RepairRecordOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            const string firstAdmin = "(SELECT Id FROM Users WHERE Role = 'admin' AND IsActive = 1 ORDER BY CreatedAt LIMIT 1)";
            foreach (var t in new[] { "Projects", "Tasks", "Updates", "Notes", "RaidItems" })
                migrationBuilder.Sql($"""
                    UPDATE {t} SET CreatedById = (SELECT u.Id FROM Users u WHERE u.DisplayName = {t}.CreatedBy)
                    WHERE CreatedById = {firstAdmin}
                      AND EXISTS (SELECT 1 FROM Users u WHERE u.DisplayName = {t}.CreatedBy AND u.Id <> {t}.CreatedById);
                    """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
