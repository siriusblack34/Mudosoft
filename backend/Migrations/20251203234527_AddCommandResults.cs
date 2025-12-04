using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MudoSoft.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CommandResults_Devices_DeviceId",
                table: "CommandResults");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CommandResults",
                table: "CommandResults");

            migrationBuilder.RenameTable(
                name: "CommandResults",
                newName: "CommandResultRecord");

            migrationBuilder.RenameIndex(
                name: "IX_CommandResults_DeviceId",
                table: "CommandResultRecord",
                newName: "IX_CommandResultRecord_DeviceId");

            migrationBuilder.RenameIndex(
                name: "IX_CommandResults_CommandId",
                table: "CommandResultRecord",
                newName: "IX_CommandResultRecord_CommandId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CommandResultRecord",
                table: "CommandResultRecord",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "ActionRecords",
                columns: table => new
                {
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExecutionDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionRecords", x => x.RecordId);
                });

            migrationBuilder.CreateTable(
                name: "StoreDevices",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreCode = table.Column<int>(type: "int", nullable: false),
                    StoreName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeviceType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DeviceName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CalculatedIpAddress = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    DbConnectionString = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSyncDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreDevices", x => x.DeviceId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreDevices_StoreCode_DeviceType",
                table: "StoreDevices",
                columns: new[] { "StoreCode", "DeviceType" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CommandResultRecord_Devices_DeviceId",
                table: "CommandResultRecord",
                column: "DeviceId",
                principalTable: "Devices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CommandResultRecord_Devices_DeviceId",
                table: "CommandResultRecord");

            migrationBuilder.DropTable(
                name: "ActionRecords");

            migrationBuilder.DropTable(
                name: "StoreDevices");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CommandResultRecord",
                table: "CommandResultRecord");

            migrationBuilder.RenameTable(
                name: "CommandResultRecord",
                newName: "CommandResults");

            migrationBuilder.RenameIndex(
                name: "IX_CommandResultRecord_DeviceId",
                table: "CommandResults",
                newName: "IX_CommandResults_DeviceId");

            migrationBuilder.RenameIndex(
                name: "IX_CommandResultRecord_CommandId",
                table: "CommandResults",
                newName: "IX_CommandResults_CommandId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CommandResults",
                table: "CommandResults",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CommandResults_Devices_DeviceId",
                table: "CommandResults",
                column: "DeviceId",
                principalTable: "Devices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
