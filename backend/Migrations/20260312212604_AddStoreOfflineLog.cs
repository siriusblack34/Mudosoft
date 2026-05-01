using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Orchestra.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreOfflineLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreOfflineLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreCode = table.Column<int>(type: "integer", nullable: false),
                    StoreName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OfflineKasaCount = table.Column<int>(type: "integer", nullable: false),
                    OfflineAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OnlineAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreOfflineLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreOfflineLogs_OfflineAt",
                table: "StoreOfflineLogs",
                column: "OfflineAt");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOfflineLogs_OnlineAt",
                table: "StoreOfflineLogs",
                column: "OnlineAt");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOfflineLogs_StoreCode",
                table: "StoreOfflineLogs",
                column: "StoreCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreOfflineLogs");
        }
    }
}
