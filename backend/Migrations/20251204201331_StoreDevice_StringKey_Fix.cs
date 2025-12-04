using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MudoSoft.Backend.Migrations
{
    /// <inheritdoc />
    public partial class StoreDevice_StringKey_Fix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) PRIMARY KEY'i kaldır
            migrationBuilder.DropPrimaryKey(
                name: "PK_StoreDevices",
                table: "StoreDevices");

            // 2) StoreName kolonunu daralt
            migrationBuilder.AlterColumn<string>(
                name: "StoreName",
                table: "StoreDevices",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            // 3) DeviceName kolonunu daralt
            migrationBuilder.AlterColumn<string>(
                name: "DeviceName",
                table: "StoreDevices",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            // 4) DeviceId kolonunu Guid → string'e dönüştür
            migrationBuilder.AlterColumn<string>(
                name: "DeviceId",
                table: "StoreDevices",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            // 5) DeviceId üzerine yeniden PRIMARY KEY ekle
            migrationBuilder.AddPrimaryKey(
                name: "PK_StoreDevices",
                table: "StoreDevices",
                column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1) PRIMARY KEY'i kaldır
            migrationBuilder.DropPrimaryKey(
                name: "PK_StoreDevices",
                table: "StoreDevices");

            // 2) StoreName kolonunu geri büyüt
            migrationBuilder.AlterColumn<string>(
                name: "StoreName",
                table: "StoreDevices",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            // 3) DeviceName kolonunu geri büyüt
            migrationBuilder.AlterColumn<string>(
                name: "DeviceName",
                table: "StoreDevices",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            // 4) DeviceId kolonunu string → Guid'e geri çevir
            migrationBuilder.AlterColumn<Guid>(
                name: "DeviceId",
                table: "StoreDevices",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            // 5) Primary Key'i geri ekle
            migrationBuilder.AddPrimaryKey(
                name: "PK_StoreDevices",
                table: "StoreDevices",
                column: "DeviceId");
        }
    }
}
