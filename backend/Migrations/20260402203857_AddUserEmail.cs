using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MudoSoft.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddUserEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Users",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeviceStatusChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StoreCode = table.Column<int>(type: "integer", nullable: false),
                    DeviceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsOnline = table.Column<bool>(type: "boolean", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceStatusChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceStatusChanges_ChangedAt",
                table: "DeviceStatusChanges",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceStatusChanges_DeviceId",
                table: "DeviceStatusChanges",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceStatusChanges_DeviceId_ChangedAt",
                table: "DeviceStatusChanges",
                columns: new[] { "DeviceId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceStatusChanges_StoreCode",
                table: "DeviceStatusChanges",
                column: "StoreCode");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceStatusChanges_StoreCode_ChangedAt",
                table: "DeviceStatusChanges",
                columns: new[] { "StoreCode", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceStatusChanges");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Users");
        }
    }
}
