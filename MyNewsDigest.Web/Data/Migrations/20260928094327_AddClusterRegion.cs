using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyNewsDigest.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClusterRegion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsInternational",
                table: "Clusters",
                type: "INTEGER",
                nullable: true);

            // Cho AI chấm lại các sự kiện gần đây để có trường region (trong nước / quốc tế)
            migrationBuilder.Sql(
                "UPDATE Clusters SET NeedsScoring = 1 WHERE LastUpdatedAt >= datetime('now', '-2 days');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsInternational",
                table: "Clusters");
        }
    }
}
