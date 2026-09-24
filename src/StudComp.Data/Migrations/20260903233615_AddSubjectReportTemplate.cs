using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudComp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubjectReportTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReportTemplateId",
                table: "Subjects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_ReportTemplateId",
                table: "Subjects",
                column: "ReportTemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_Subjects_ReportTemplates_ReportTemplateId",
                table: "Subjects",
                column: "ReportTemplateId",
                principalTable: "ReportTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subjects_ReportTemplates_ReportTemplateId",
                table: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_ReportTemplateId",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "ReportTemplateId",
                table: "Subjects");
        }
    }
}
