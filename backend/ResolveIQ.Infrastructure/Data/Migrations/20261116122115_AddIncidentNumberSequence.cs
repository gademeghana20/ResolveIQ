using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResolveIQ.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentNumberSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "IncidentNumberSeq");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "IncidentNumberSeq");
        }
    }
}
