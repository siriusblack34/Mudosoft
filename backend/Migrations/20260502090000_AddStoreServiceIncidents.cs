using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Orchestra.Backend.Data;

#nullable disable

namespace Orchestra.Backend.Migrations
{
    [DbContext(typeof(OrchestraDbContext))]
    [Migration("20260502090000_AddStoreServiceIncidents")]
    public partial class AddStoreServiceIncidents : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreServiceIncidents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StoreCode = table.Column<int>(type: "integer", nullable: false),
                    StoreName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    ServiceName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LastStartMode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    FirstDetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastDetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreServiceIncidents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreServiceIncidents_DeviceId",
                table: "StoreServiceIncidents",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreServiceIncidents_DeviceId_ServiceName_ResolvedAt",
                table: "StoreServiceIncidents",
                columns: new[] { "DeviceId", "ServiceName", "ResolvedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreServiceIncidents_LastDetectedAt",
                table: "StoreServiceIncidents",
                column: "LastDetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StoreServiceIncidents_ResolvedAt",
                table: "StoreServiceIncidents",
                column: "ResolvedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StoreServiceIncidents_ServiceName",
                table: "StoreServiceIncidents",
                column: "ServiceName");

            migrationBuilder.CreateIndex(
                name: "IX_StoreServiceIncidents_StoreCode",
                table: "StoreServiceIncidents",
                column: "StoreCode");

            migrationBuilder.CreateIndex(
                name: "IX_StoreServiceIncidents_StoreCode_ResolvedAt",
                table: "StoreServiceIncidents",
                columns: new[] { "StoreCode", "ResolvedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_StoreServiceIncidents_Active_Device_Service",
                table: "StoreServiceIncidents",
                columns: new[] { "DeviceId", "ServiceName" },
                unique: true,
                filter: "\"ResolvedAt\" IS NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StoreServiceIncidents");
        }
    }
}
