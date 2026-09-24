using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudComp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CardDecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ColorHex = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    QueryExpression = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardDecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardDecks_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CardTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ColorHex = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    UsageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeckId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Front = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Back = table.Column<string>(type: "TEXT", nullable: false),
                    Hint = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    IsPinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSuspended = table.Column<bool>(type: "INTEGER", nullable: false),
                    Difficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SourceNoteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceFileRecordId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DueAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IntervalDays = table.Column<double>(type: "REAL", nullable: false),
                    EaseFactor = table.Column<double>(type: "REAL", nullable: false),
                    Repetitions = table.Column<int>(type: "INTEGER", nullable: false),
                    Lapses = table.Column<int>(type: "INTEGER", nullable: false),
                    LastReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SchedulerName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cards_CardDecks_DeckId",
                        column: x => x.DeckId,
                        principalTable: "CardDecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Cards_FileRecords_SourceFileRecordId",
                        column: x => x.SourceFileRecordId,
                        principalTable: "FileRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Cards_Notes_SourceNoteId",
                        column: x => x.SourceNoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Cards_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "StudySessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeckId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FilterJson = table.Column<string>(type: "TEXT", nullable: false),
                    Seed = table.Column<int>(type: "INTEGER", nullable: false),
                    PlannedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AnsweredCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CorrectCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeLimitSeconds = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudySessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudySessions_CardDecks_DeckId",
                        column: x => x.DeckId,
                        principalTable: "CardDecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_StudySessions_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CardTagLinks",
                columns: table => new
                {
                    CardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TagId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardTagLinks", x => new { x.CardId, x.TagId });
                    table.ForeignKey(
                        name: "FK_CardTagLinks_CardTags_TagId",
                        column: x => x.TagId,
                        principalTable: "CardTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CardTagLinks_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CardReviewLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Grade = table.Column<int>(type: "INTEGER", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    ElapsedMs = table.Column<int>(type: "INTEGER", nullable: false),
                    IntervalBeforeDays = table.Column<double>(type: "REAL", nullable: false),
                    IntervalAfterDays = table.Column<double>(type: "REAL", nullable: false),
                    EaseBefore = table.Column<double>(type: "REAL", nullable: false),
                    EaseAfter = table.Column<double>(type: "REAL", nullable: false),
                    WasCorrect = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardReviewLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardReviewLogs_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CardReviewLogs_StudySessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "StudySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardDecks_SubjectId",
                table: "CardDecks",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CardReviewLogs_CardId",
                table: "CardReviewLogs",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_CardReviewLogs_ReviewedAt",
                table: "CardReviewLogs",
                column: "ReviewedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CardReviewLogs_SessionId",
                table: "CardReviewLogs",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_DeckId",
                table: "Cards",
                column: "DeckId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_DeletedAt",
                table: "Cards",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_DeletedAt_DueAt",
                table: "Cards",
                columns: new[] { "DeletedAt", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_DueAt",
                table: "Cards",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_SourceFileRecordId",
                table: "Cards",
                column: "SourceFileRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_SourceNoteId",
                table: "Cards",
                column: "SourceNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_SubjectId",
                table: "Cards",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_UpdatedAt",
                table: "Cards",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CardTagLinks_TagId",
                table: "CardTagLinks",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_CardTags_Name",
                table: "CardTags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardTags_UsageCount",
                table: "CardTags",
                column: "UsageCount");

            migrationBuilder.CreateIndex(
                name: "IX_StudySessions_DeckId",
                table: "StudySessions",
                column: "DeckId");

            migrationBuilder.CreateIndex(
                name: "IX_StudySessions_StartedAt",
                table: "StudySessions",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StudySessions_SubjectId",
                table: "StudySessions",
                column: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardReviewLogs");

            migrationBuilder.DropTable(
                name: "CardTagLinks");

            migrationBuilder.DropTable(
                name: "StudySessions");

            migrationBuilder.DropTable(
                name: "CardTags");

            migrationBuilder.DropTable(
                name: "Cards");

            migrationBuilder.DropTable(
                name: "CardDecks");
        }
    }
}
