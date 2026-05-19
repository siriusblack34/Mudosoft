using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Orchestra.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreOpeningChecklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoreOpeningActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreOpeningId = table.Column<int>(type: "integer", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreOpeningActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoreOpenings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreCode = table.Column<int>(type: "integer", nullable: false),
                    StoreName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    City = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TargetOpeningDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActualOpeningDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TemplateId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RoleAssignmentsJson = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreOpenings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoreOpeningTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreOpeningTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoreOpeningItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreOpeningId = table.Column<int>(type: "integer", nullable: false),
                    CategoryName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AssignedRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ItemName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    HasSerialNumber = table.Column<bool>(type: "boolean", nullable: false),
                    HasAssetNumber = table.Column<bool>(type: "boolean", nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AssetNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PhotoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CompletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreOpeningItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreOpeningItems_StoreOpenings_StoreOpeningId",
                        column: x => x.StoreOpeningId,
                        principalTable: "StoreOpenings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreOpeningTemplateItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TemplateId = table.Column<int>(type: "integer", nullable: false),
                    CategoryName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AssignedRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ItemName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    HasSerialNumber = table.Column<bool>(type: "boolean", nullable: false),
                    HasAssetNumber = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreOpeningTemplateItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreOpeningTemplateItems_StoreOpeningTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "StoreOpeningTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreOpeningActivities_CreatedAt",
                table: "StoreOpeningActivities",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOpeningActivities_StoreOpeningId",
                table: "StoreOpeningActivities",
                column: "StoreOpeningId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOpeningItems_AssignedRole",
                table: "StoreOpeningItems",
                column: "AssignedRole");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOpeningItems_Status",
                table: "StoreOpeningItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOpeningItems_StoreOpeningId",
                table: "StoreOpeningItems",
                column: "StoreOpeningId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOpeningItems_StoreOpeningId_CategoryName",
                table: "StoreOpeningItems",
                columns: new[] { "StoreOpeningId", "CategoryName" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreOpenings_Status",
                table: "StoreOpenings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOpenings_StoreCode",
                table: "StoreOpenings",
                column: "StoreCode");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOpenings_TargetOpeningDate",
                table: "StoreOpenings",
                column: "TargetOpeningDate");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOpeningTemplateItems_TemplateId",
                table: "StoreOpeningTemplateItems",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreOpeningTemplates_IsDefault",
                table: "StoreOpeningTemplates",
                column: "IsDefault");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreOpeningActivities");

            migrationBuilder.DropTable(
                name: "StoreOpeningItems");

            migrationBuilder.DropTable(
                name: "StoreOpeningTemplateItems");

            migrationBuilder.DropTable(
                name: "StoreOpenings");

            migrationBuilder.DropTable(
                name: "StoreOpeningTemplates");
        }
    }
}
