using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MudoSoft.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddTemporaryCloseToDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    ALTER TABLE ""Devices"" ADD COLUMN ""IsTemporarilyClosed"" boolean NOT NULL DEFAULT FALSE;
                EXCEPTION WHEN duplicate_column THEN NULL;
                END $$;
            ");
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    ALTER TABLE ""Devices"" ADD COLUMN ""TemporaryCloseReason"" text;
                EXCEPTION WHEN duplicate_column THEN NULL;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTemporarilyClosed",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "TemporaryCloseReason",
                table: "Devices");
        }
    }
}
