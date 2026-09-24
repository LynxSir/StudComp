using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudComp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSemester : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Semester",
                table: "Subjects");

            migrationBuilder.AddColumn<Guid>(
                name: "SemesterId",
                table: "Subjects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Semesters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CourseNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    FirstWeekIsOdd = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    PairSlotsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Semesters", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_SemesterId",
                table: "Subjects",
                column: "SemesterId");

            migrationBuilder.CreateIndex(
                name: "IX_Semesters_IsActive",
                table: "Semesters",
                column: "IsActive");

            migrationBuilder.AddForeignKey(
                name: "FK_Subjects_Semesters_SemesterId",
                table: "Subjects",
                column: "SemesterId",
                principalTable: "Semesters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subjects_Semesters_SemesterId",
                table: "Subjects");

            migrationBuilder.DropTable(
                name: "Semesters");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_SemesterId",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "SemesterId",
                table: "Subjects");

            migrationBuilder.AddColumn<int>(
                name: "Semester",
                table: "Subjects",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
