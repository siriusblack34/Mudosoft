using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Orchestra.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddOnCallAndPlaybooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnCallWorkdays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FullName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WorkDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DayType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnCallWorkdays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RemediationPlaybooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TriggerType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TriggerConditionJson = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemediationPlaybooks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlaybookExecutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlaybookId = table.Column<int>(type: "integer", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Hostname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StoreCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TriggerReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybookExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaybookExecutions_RemediationPlaybooks_PlaybookId",
                        column: x => x.PlaybookId,
                        principalTable: "RemediationPlaybooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaybookSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlaybookId = table.Column<int>(type: "integer", nullable: false),
                    StepOrder = table.Column<int>(type: "integer", nullable: false),
                    ActionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ActionPayloadJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DelaySeconds = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybookSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaybookSteps_RemediationPlaybooks_PlaybookId",
                        column: x => x.PlaybookId,
                        principalTable: "RemediationPlaybooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnCallWorkdays_UserId",
                table: "OnCallWorkdays",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OnCallWorkdays_UserId_WorkDate",
                table: "OnCallWorkdays",
                columns: new[] { "UserId", "WorkDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnCallWorkdays_Username",
                table: "OnCallWorkdays",
                column: "Username");

            migrationBuilder.CreateIndex(
                name: "IX_OnCallWorkdays_WorkDate",
                table: "OnCallWorkdays",
                column: "WorkDate");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybookExecutions_DeviceId",
                table: "PlaybookExecutions",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybookExecutions_PlaybookId",
                table: "PlaybookExecutions",
                column: "PlaybookId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybookExecutions_StartedAt",
                table: "PlaybookExecutions",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybookExecutions_Status",
                table: "PlaybookExecutions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybookSteps_PlaybookId",
                table: "PlaybookSteps",
                column: "PlaybookId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybookSteps_PlaybookId_StepOrder",
                table: "PlaybookSteps",
                columns: new[] { "PlaybookId", "StepOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RemediationPlaybooks_IsEnabled",
                table: "RemediationPlaybooks",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationPlaybooks_TriggerType",
                table: "RemediationPlaybooks",
                column: "TriggerType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnCallWorkdays");

            migrationBuilder.DropTable(
                name: "PlaybookExecutions");

            migrationBuilder.DropTable(
                name: "PlaybookSteps");

            migrationBuilder.DropTable(
                name: "RemediationPlaybooks");
        }
    }
}
