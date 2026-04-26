using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MudoSoft.Backend.Data;

#nullable disable

namespace MudoSoft.Backend.Migrations
{
    [DbContext(typeof(MudoSoftDbContext))]
    [Migration("20260411111000_AddAgendaItems")]
    public partial class AddAgendaItems : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgendaItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgendaItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgendaItems_DueDate",
                table: "AgendaItems",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_AgendaItems_Priority",
                table: "AgendaItems",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_AgendaItems_Status",
                table: "AgendaItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AgendaItems_UpdatedAt",
                table: "AgendaItems",
                column: "UpdatedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgendaItems");
        }
    }
}
