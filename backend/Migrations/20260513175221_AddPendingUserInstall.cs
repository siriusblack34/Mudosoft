using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Orchestra.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingUserInstall : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DcLogCursors",
                columns: table => new
                {
                    DcName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LastRecordId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DcLogCursors", x => x.DcName);
                });

            migrationBuilder.CreateTable(
                name: "PendingUserInstalls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SamAccountName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MatchedComputer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    MatchedIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InstallId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingUserInstalls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingUserInstalls_ExpiresAt",
                table: "PendingUserInstalls",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PendingUserInstalls_SamAccountName",
                table: "PendingUserInstalls",
                column: "SamAccountName");

            migrationBuilder.CreateIndex(
                name: "IX_PendingUserInstalls_Status",
                table: "PendingUserInstalls",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PendingUserInstalls_Status_SamAccountName",
                table: "PendingUserInstalls",
                columns: new[] { "Status", "SamAccountName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DcLogCursors");

            migrationBuilder.DropTable(
                name: "PendingUserInstalls");
        }
    }
}
