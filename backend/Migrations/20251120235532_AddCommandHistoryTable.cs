using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MudoSoft.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandHistoryTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeviceMetrics_DeviceId_TimestampUtc",
                table: "DeviceMetrics");

            migrationBuilder.AlterColumn<string>(
                name: "StoreName",
                table: "Devices",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "IpAddress",
                table: "Devices",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "Devices",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "DeviceId",
                table: "DeviceMetrics",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)");

            migrationBuilder.CreateTable(
                name: "CommandResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CommandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CommandType = table.Column<int>(type: "int", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    Output = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommandResults_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceMetrics_DeviceId",
                table: "DeviceMetrics",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_CommandResults_CommandId",
                table: "CommandResults",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommandResults_DeviceId",
                table: "CommandResults",
                column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommandResults");

            migrationBuilder.DropIndex(
                name: "IX_DeviceMetrics_DeviceId",
                table: "DeviceMetrics");

            migrationBuilder.AlterColumn<string>(
                name: "StoreName",
                table: "Devices",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "IpAddress",
                table: "Devices",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "Devices",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "DeviceId",
                table: "DeviceMetrics",
                type: "nvarchar(100)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceMetrics_DeviceId_TimestampUtc",
                table: "DeviceMetrics",
                columns: new[] { "DeviceId", "TimestampUtc" });
        }
    }
}
