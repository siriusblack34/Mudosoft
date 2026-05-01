using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Orchestra.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddTemporaryCloseToStoreDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTemporarilyClosed",
                table: "StoreDevices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TemporaryCloseReason",
                table: "StoreDevices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // LastSeen, StoreManagers, indexes already exist in DB - skip
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTemporarilyClosed",
                table: "StoreDevices");

            migrationBuilder.DropColumn(
                name: "TemporaryCloseReason",
                table: "StoreDevices");
        }
    }
}
