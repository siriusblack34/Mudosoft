using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MudoSoft.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddHardwareInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CpuModel",
                table: "Devices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GpuModel",
                table: "Devices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastLoggedInUser",
                table: "Devices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SystemBootTime",
                table: "Devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TotalDiskGB",
                table: "Devices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TotalRamMB",
                table: "Devices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CpuModel",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "GpuModel",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LastLoggedInUser",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "SystemBootTime",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "TotalDiskGB",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "TotalRamMB",
                table: "Devices");
        }
    }
}
