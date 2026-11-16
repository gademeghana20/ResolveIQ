using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResolveIQ.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSlaClockAndPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Priority",
                table: "Incidents",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<DateTime>(
                name: "SlaPausedAtUtc",
                table: "Incidents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SlaPausedSeconds",
                table: "Incidents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "SlaStartedAtUtc",
                table: "Incidents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SlaPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    ResolutionTargetHours = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlaPolicies", x => x.Id);
                    table.CheckConstraint("CK_SlaPolicies_ResolutionTargetHours", "[ResolutionTargetHours] BETWEEN 1 AND 720");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlaPolicies_Priority",
                table: "SlaPolicies",
                column: "Priority",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlaPolicies");

            migrationBuilder.DropColumn(
                name: "SlaPausedAtUtc",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "SlaPausedSeconds",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "SlaStartedAtUtc",
                table: "Incidents");

            migrationBuilder.AlterColumn<int>(
                name: "Priority",
                table: "Incidents",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
