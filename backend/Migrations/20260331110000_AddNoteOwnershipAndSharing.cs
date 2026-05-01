using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Orchestra.Backend.Data;

#nullable disable

namespace Orchestra.Backend.Migrations
{
    [DbContext(typeof(OrchestraDbContext))]
    [Migration("20260331110000_AddNoteOwnershipAndSharing")]
    public partial class AddNoteOwnershipAndSharing : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsShared",
                table: "Notes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OwnerUsername",
                table: "Notes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "admin");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_IsShared",
                table: "Notes",
                column: "IsShared");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_OwnerUsername",
                table: "Notes",
                column: "OwnerUsername");

            migrationBuilder.Sql("""
                UPDATE "Notes"
                SET "OwnerUsername" = 'admin'
                WHERE "OwnerUsername" IS NULL OR BTRIM("OwnerUsername") = '';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notes_IsShared",
                table: "Notes");

            migrationBuilder.DropIndex(
                name: "IX_Notes_OwnerUsername",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "IsShared",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "OwnerUsername",
                table: "Notes");
        }
    }
}
