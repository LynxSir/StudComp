using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudComp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    RefId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityLog_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLog_Kind",
                table: "ActivityLog",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLog_SubjectId",
                table: "ActivityLog",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLog_Timestamp",
                table: "ActivityLog",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLog");
        }
    }
}
