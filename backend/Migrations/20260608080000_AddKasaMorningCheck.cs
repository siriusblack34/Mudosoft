using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Orchestra.Backend.Data;

#nullable disable

namespace Orchestra.Backend.Migrations
{
    [DbContext(typeof(OrchestraDbContext))]
    [Migration("20260608080000_AddKasaMorningCheck")]
    public partial class AddKasaMorningCheck : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KasaMorningChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreDeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StoreCode = table.Column<int>(type: "integer", nullable: false),
                    StoreName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeviceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsUncReachable = table.Column<bool>(type: "boolean", nullable: false),
                    IsGeniusPosLogFound = table.Column<bool>(type: "boolean", nullable: false),
                    IsHealthy = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KasaMorningChecks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KasaMorningChecks_CheckedAt",
                table: "KasaMorningChecks",
                column: "CheckedAt");

            migrationBuilder.CreateIndex(
                name: "IX_KasaMorningChecks_IsHealthy",
                table: "KasaMorningChecks",
                column: "IsHealthy");

            migrationBuilder.CreateIndex(
                name: "IX_KasaMorningChecks_StoreCode",
                table: "KasaMorningChecks",
                column: "StoreCode");

            migrationBuilder.CreateIndex(
                name: "IX_KasaMorningChecks_StoreDeviceId_CheckedAt",
                table: "KasaMorningChecks",
                columns: new[] { "StoreDeviceId", "CheckedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "KasaMorningChecks");
        }
    }
}
