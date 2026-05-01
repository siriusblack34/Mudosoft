using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orchestra.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddOsToDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StoreDevices_StoreCode_DeviceType",
                table: "StoreDevices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StoreDevices_StoreCode_DeviceType",
                table: "StoreDevices",
                columns: new[] { "StoreCode", "DeviceType" },
                unique: true);
        }
    }
}
