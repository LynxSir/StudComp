using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudComp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArchivistFolderScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WatchedFolder",
                table: "ArchivistRules",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileRecords_OriginalPath",
                table: "FileRecords",
                column: "OriginalPath");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FileRecords_OriginalPath",
                table: "FileRecords");

            migrationBuilder.DropColumn(
                name: "WatchedFolder",
                table: "ArchivistRules");
        }
    }
}
