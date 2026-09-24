using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudComp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFileOperationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileOperationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileRecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OriginalPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    PlannedPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FinalPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileOperationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileOperationLogs_ArchivistRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "ArchivistRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FileOperationLogs_FileRecords_FileRecordId",
                        column: x => x.FileRecordId,
                        principalTable: "FileRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileOperationLogs_FileRecordId",
                table: "FileOperationLogs",
                column: "FileRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_FileOperationLogs_RuleId",
                table: "FileOperationLogs",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_FileOperationLogs_StartedAt",
                table: "FileOperationLogs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FileOperationLogs_Status",
                table: "FileOperationLogs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileOperationLogs");
        }
    }
}
