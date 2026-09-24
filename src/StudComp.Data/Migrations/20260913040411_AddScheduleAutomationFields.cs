using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudComp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleAutomationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TeacherFullName",
                table: "Subjects",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultBreakMinutes",
                table: "Semesters",
                type: "INTEGER",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "LunchBreakEnd",
                table: "Semesters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "LunchBreakStart",
                table: "Semesters",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TeacherFullName",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "DefaultBreakMinutes",
                table: "Semesters");

            migrationBuilder.DropColumn(
                name: "LunchBreakEnd",
                table: "Semesters");

            migrationBuilder.DropColumn(
                name: "LunchBreakStart",
                table: "Semesters");
        }
    }
}
