using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Orchestra.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "StoreCode",
                table: "StoreNetworkInfos",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.CreateTable(
                name: "InventoryImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ImportedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    InsertedCount = table.Column<int>(type: "integer", nullable: false),
                    UpdatedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    UnmatchedStoreCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoreNameMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RawName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StoreCode = table.Column<int>(type: "integer", nullable: true),
                    AutoMatched = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreNameMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssetName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StoreCode = table.Column<int>(type: "integer", nullable: true),
                    StoreNameRaw = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProductType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Product = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProductCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OrgSerialNumber = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ComputerName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MacAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AssetTag = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AcquisitionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    YazarkasaSicilNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BaseSeriNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PrinterSeriNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IkinciMonitorSeriNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AssetState = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FizikselDurum = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PurchaseCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    FaturaNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TalepNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExtraJson = table.Column<string>(type: "text", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ImportBatchId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryAssets_InventoryImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "InventoryImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAssets_AssetName",
                table: "InventoryAssets",
                column: "AssetName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAssets_AssetState",
                table: "InventoryAssets",
                column: "AssetState");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAssets_ImportBatchId",
                table: "InventoryAssets",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAssets_ProductType",
                table: "InventoryAssets",
                column: "ProductType");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAssets_StoreCode",
                table: "InventoryAssets",
                column: "StoreCode");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryImportBatches_ImportedAt",
                table: "InventoryImportBatches",
                column: "ImportedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StoreNameMappings_RawName",
                table: "StoreNameMappings",
                column: "RawName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreNameMappings_StoreCode",
                table: "StoreNameMappings",
                column: "StoreCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryAssets");

            migrationBuilder.DropTable(
                name: "StoreNameMappings");

            migrationBuilder.DropTable(
                name: "InventoryImportBatches");

            migrationBuilder.AlterColumn<int>(
                name: "StoreCode",
                table: "StoreNetworkInfos",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
        }
    }
}
