using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResolveIQ.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Incidents_AssignedToUserId",
                table: "Incidents",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_CreatedAtUtc",
                table: "Incidents",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_IncidentNumber",
                table: "Incidents",
                column: "IncidentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Status",
                table: "Incidents",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Incidents_AssignedToUserId",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_CreatedAtUtc",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_IncidentNumber",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_Status",
                table: "Incidents");
        }
    }
}
