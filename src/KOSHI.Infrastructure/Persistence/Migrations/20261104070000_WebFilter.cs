using System;
using KOSHI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOSHI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(KoshiDbContext))]
    [Migration("20261104070000_WebFilter")]
    public partial class WebFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlockedSites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedSites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteAllowances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockedSiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteAllowances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteAllowances_BlockedSites_BlockedSiteId",
                        column: x => x.BlockedSiteId,
                        principalTable: "BlockedSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SiteAllowances_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockedSites_Domain",
                table: "BlockedSites",
                column: "Domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteAllowances_BlockedSiteId_ExpiresAt",
                table: "SiteAllowances",
                columns: new[] { "BlockedSiteId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteAllowances_DeviceId",
                table: "SiteAllowances",
                column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteAllowances");

            migrationBuilder.DropTable(
                name: "BlockedSites");
        }
    }
}
