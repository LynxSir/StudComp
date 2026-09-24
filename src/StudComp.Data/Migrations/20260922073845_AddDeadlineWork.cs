using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudComp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeadlineWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeadlineId",
                table: "Notes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AnsweredAt",
                table: "Deadlines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FolderName",
                table: "Deadlines",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeadlineAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeadlineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeadlineAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeadlineAttachments_Deadlines_DeadlineId",
                        column: x => x.DeadlineId,
                        principalTable: "Deadlines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_DeadlineId",
                table: "Notes",
                column: "DeadlineId");

            migrationBuilder.CreateIndex(
                name: "IX_DeadlineAttachments_DeadlineId",
                table: "DeadlineAttachments",
                column: "DeadlineId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_Deadlines_DeadlineId",
                table: "Notes",
                column: "DeadlineId",
                principalTable: "Deadlines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notes_Deadlines_DeadlineId",
                table: "Notes");

            migrationBuilder.DropTable(
                name: "DeadlineAttachments");

            migrationBuilder.DropIndex(
                name: "IX_Notes_DeadlineId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "DeadlineId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "AnsweredAt",
                table: "Deadlines");

            migrationBuilder.DropColumn(
                name: "FolderName",
                table: "Deadlines");
        }
    }
}
