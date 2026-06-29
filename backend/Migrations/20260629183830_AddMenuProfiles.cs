using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Orchestra.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MenuDenialsJson",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MenuGrantsJson",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MenuProfileId",
                table: "Users",
                type: "integer",
                nullable: true);

            // NOT: Otomatik diff, snapshot drift'i yüzünden 'WindowsVersion' (StoreDevices) kolonunu ve
            // 'DeviceCredentials' tablosunu da bu migration'a eklemişti. Bunlar bu özelliğe ait değil ve
            // prod'da zaten mevcut; Database.Migrate() çökmesini önlemek için bilinçli olarak çıkarıldı.

            migrationBuilder.CreateTable(
                name: "MenuProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    AllowAllByDefault = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedMenusJson = table.Column<string>(type: "text", nullable: false),
                    HiddenMenusJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_MenuProfileId",
                table: "Users",
                column: "MenuProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuProfiles_Name",
                table: "MenuProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_MenuProfiles_MenuProfileId",
                table: "Users",
                column: "MenuProfileId",
                principalTable: "MenuProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_MenuProfiles_MenuProfileId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "MenuProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Users_MenuProfileId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MenuDenialsJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MenuGrantsJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MenuProfileId",
                table: "Users");
        }
    }
}
