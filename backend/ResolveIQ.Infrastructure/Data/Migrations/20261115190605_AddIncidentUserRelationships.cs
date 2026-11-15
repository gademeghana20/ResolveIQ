using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResolveIQ.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentUserRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Incidents_ReportedByUserId",
                table: "Incidents",
                column: "ReportedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Incidents_AspNetUsers_AssignedToUserId",
                table: "Incidents",
                column: "AssignedToUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Incidents_AspNetUsers_ReportedByUserId",
                table: "Incidents",
                column: "ReportedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Incidents_AspNetUsers_AssignedToUserId",
                table: "Incidents");

            migrationBuilder.DropForeignKey(
                name: "FK_Incidents_AspNetUsers_ReportedByUserId",
                table: "Incidents");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_ReportedByUserId",
                table: "Incidents");
        }
    }
}
