using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudComp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGradeForecastFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ForecastStrategyName",
                table: "Subjects",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "GradeScaleKind",
                table: "Subjects",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "GradeScaleMax",
                table: "Subjects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GradeScalePassThreshold",
                table: "Subjects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPlanned",
                table: "GradeEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "GradeEntries",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ForecastStrategyName",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "GradeScaleKind",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "GradeScaleMax",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "GradeScalePassThreshold",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "IsPlanned",
                table: "GradeEntries");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "GradeEntries");
        }
    }
}
