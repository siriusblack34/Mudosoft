using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orchestra.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddDiskDDriveColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "CurrentDiskDUsagePercent",
                table: "Devices",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TotalDiskDGB",
                table: "Devices",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentDiskDUsagePercent",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "TotalDiskDGB",
                table: "Devices");
        }
    }
}
