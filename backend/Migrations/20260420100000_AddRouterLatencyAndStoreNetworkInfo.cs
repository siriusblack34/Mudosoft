using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MudoSoft.Backend.Data;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MudoSoft.Backend.Migrations
{
    [DbContext(typeof(MudoSoftDbContext))]
    [Migration("20260420100000_AddRouterLatencyAndStoreNetworkInfo")]
    public partial class AddRouterLatencyAndStoreNetworkInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // RouterLatencySamples
            migrationBuilder.CreateTable(
                name: "RouterLatencySamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StoreCode = table.Column<int>(type: "integer", nullable: false),
                    Ip = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    RttMs = table.Column<int>(type: "integer", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    SampledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouterLatencySamples", x => x.Id);
                });

            migrationBuilder.CreateIndex(name: "IX_RouterLatencySamples_StoreCode", table: "RouterLatencySamples", column: "StoreCode");
            migrationBuilder.CreateIndex(name: "IX_RouterLatencySamples_SampledAt", table: "RouterLatencySamples", column: "SampledAt");
            migrationBuilder.CreateIndex(name: "IX_RouterLatencySamples_StoreCode_SampledAt", table: "RouterLatencySamples", columns: new[] { "StoreCode", "SampledAt" });
            migrationBuilder.CreateIndex(name: "IX_RouterLatencySamples_DeviceId_SampledAt", table: "RouterLatencySamples", columns: new[] { "DeviceId", "SampledAt" });

            // StoreNetworkInfos
            migrationBuilder.CreateTable(
                name: "StoreNetworkInfos",
                columns: table => new
                {
                    StoreCode = table.Column<int>(type: "integer", nullable: false),
                    TerrestrialMbps = table.Column<int>(type: "integer", nullable: false),
                    LineType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Notes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreNetworkInfos", x => x.StoreCode);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RouterLatencySamples");
            migrationBuilder.DropTable(name: "StoreNetworkInfos");
        }
    }
}
