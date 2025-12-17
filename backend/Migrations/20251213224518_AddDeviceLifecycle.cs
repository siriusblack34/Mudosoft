using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MudoSoft.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "StoreDevices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSeenAt",
                table: "StoreDevices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "StoreDevices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "StoreDevices");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "StoreDevices");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "StoreDevices");
        }
    }
}
