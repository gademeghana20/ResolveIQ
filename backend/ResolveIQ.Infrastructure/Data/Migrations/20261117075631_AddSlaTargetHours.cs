using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResolveIQ.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSlaTargetHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SlaTargetHours",
                table: "Incidents",
                type: "int",
                nullable: true);

            // Incidents whose clock has already started were being measured against today's policy.
            // Give each of them those hours, so nothing about them changes when this migration is applied.
            // Incidents that never started have no clock, and ones whose priority has no active policy
            // stay "no SLA" (null), exactly as they show today.
            migrationBuilder.Sql(@"
                UPDATE i
                SET i.SlaTargetHours = p.ResolutionTargetHours
                FROM Incidents AS i
                INNER JOIN SlaPolicies AS p ON p.Priority = i.Priority AND p.IsActive = 1
                WHERE i.SlaStartedAtUtc IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SlaTargetHours",
                table: "Incidents");
        }
    }
}
