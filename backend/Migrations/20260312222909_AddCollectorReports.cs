using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MudoSoft.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectorReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CollectorReports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CollectorName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    JsonData = table.Column<string>(type: "text", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectorReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectorReports_CollectorName",
                table: "CollectorReports",
                column: "CollectorName");

            migrationBuilder.CreateIndex(
                name: "IX_CollectorReports_DeviceId",
                table: "CollectorReports",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectorReports_DeviceId_CollectorName_TimestampUtc",
                table: "CollectorReports",
                columns: new[] { "DeviceId", "CollectorName", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectorReports_DeviceId_TimestampUtc",
                table: "CollectorReports",
                columns: new[] { "DeviceId", "TimestampUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectorReports");
        }
    }
}
