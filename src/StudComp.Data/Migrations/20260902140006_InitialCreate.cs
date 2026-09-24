using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudComp.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    GostVariant = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StyleProfileJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Semester = table.Column<int>(type: "INTEGER", nullable: false),
                    FolderPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ColorHex = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArchivistRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Pattern = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    MatchType = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RenameTemplate = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchivistRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchivistRules_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FileRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OriginalPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CurrentPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileRecords_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "GradeEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    RawScore = table.Column<string>(type: "TEXT", nullable: false),
                    MaxScore = table.Column<string>(type: "TEXT", nullable: false),
                    Weight = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Semester = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeEntries_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TemplateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourcePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    OutputPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportJobs_ReportTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ReportTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportJobs_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    Room = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Teacher = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    WeekParity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleEntries_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Deadlines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    DueDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LinkedFileRecordId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deadlines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deadlines_FileRecords_LinkedFileRecordId",
                        column: x => x.LinkedFileRecordId,
                        principalTable: "FileRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Deadlines_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchivistRules_Priority",
                table: "ArchivistRules",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_ArchivistRules_SubjectId",
                table: "ArchivistRules",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Deadlines_DueDate",
                table: "Deadlines",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Deadlines_LinkedFileRecordId",
                table: "Deadlines",
                column: "LinkedFileRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_Deadlines_Status",
                table: "Deadlines",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Deadlines_SubjectId",
                table: "Deadlines",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_FileRecords_ContentHash",
                table: "FileRecords",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_FileRecords_Status",
                table: "FileRecords",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FileRecords_SubjectId",
                table: "FileRecords",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_GradeEntries_SubjectId",
                table: "GradeEntries",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportJobs_CreatedAt",
                table: "ReportJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReportJobs_SubjectId",
                table: "ReportJobs",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportJobs_TemplateId",
                table: "ReportJobs",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleEntries_SubjectId",
                table: "ScheduleEntries",
                column: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchivistRules");

            migrationBuilder.DropTable(
                name: "Deadlines");

            migrationBuilder.DropTable(
                name: "GradeEntries");

            migrationBuilder.DropTable(
                name: "ReportJobs");

            migrationBuilder.DropTable(
                name: "ScheduleEntries");

            migrationBuilder.DropTable(
                name: "FileRecords");

            migrationBuilder.DropTable(
                name: "ReportTemplates");

            migrationBuilder.DropTable(
                name: "Subjects");
        }
    }
}
