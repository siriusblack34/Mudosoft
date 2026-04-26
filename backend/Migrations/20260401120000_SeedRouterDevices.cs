using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MudoSoft.Backend.Migrations
{
    /// <inheritdoc />
    public partial class SeedRouterDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Her mağaza için bir Router kaydı ekle
            // IP: 192.168.{StoreCode}.1 (istisna: Kaş Marina / 257 → 192.168.50.1)
            // Aynı DeviceId varsa ekleme (idempotent)
            migrationBuilder.Sql(@"
                INSERT INTO ""StoreDevices"" (
                    ""DeviceId"", ""StoreCode"", ""StoreName"", ""DeviceType"", ""DeviceName"",
                    ""CalculatedIpAddress"", ""DbConnectionString"",
                    ""CreatedDate"", ""LastSyncDate"", ""IsTemporarilyClosed""
                )
                SELECT
                    'RTR-' || sub.""StoreCode"",
                    sub.""StoreCode"",
                    sub.""StoreName"",
                    'ROUTER',
                    'Router',
                    CASE
                        WHEN sub.""StoreCode"" = 257 THEN '192.168.50.1'
                        ELSE '192.168.' || sub.""StoreCode"" || '.1'
                    END,
                    '',
                    NOW(),
                    NOW(),
                    false
                FROM (
                    SELECT DISTINCT ON (""StoreCode"") ""StoreCode"", ""StoreName""
                    FROM ""StoreDevices""
                    WHERE ""StoreCode"" > 1
                    ORDER BY ""StoreCode"", ""StoreName""
                ) sub
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""StoreDevices"" sd
                    WHERE sd.""DeviceId"" = 'RTR-' || sub.""StoreCode""
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM ""StoreDevices"" WHERE ""DeviceType"" = 'ROUTER';
            ");
        }
    }
}
