using System;
using KOSHI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOSHI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(KoshiDbContext))]
    [Migration("20261104060000_DeviceLocations")]
    public partial class DeviceLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    AccuracyMeters = table.Column<double>(type: "double precision", nullable: true),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    City = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Region = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Country = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Isp = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PublicIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceLocations_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceLocations_DeviceId_LastSeenAt",
                table: "DeviceLocations",
                columns: new[] { "DeviceId", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceLocations_LastSeenAt",
                table: "DeviceLocations",
                column: "LastSeenAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceLocations");
        }
    }
}
