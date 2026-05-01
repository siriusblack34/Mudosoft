using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orchestra.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddExcludeFromOfflineListToDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    ALTER TABLE ""Devices"" ADD COLUMN ""ExcludeFromOfflineList"" boolean NOT NULL DEFAULT FALSE;
                EXCEPTION WHEN duplicate_column THEN NULL;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcludeFromOfflineList",
                table: "Devices");
        }
    }
}
