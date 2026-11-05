using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOSHI.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(KoshiDbContext))]
    [Migration("20261105140000_DeviceLicense")]
    public partial class DeviceLicense : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LicenseExpiresAt",
                table: "Devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LicenseDays",
                table: "DeviceActivations",
                type: "integer",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LicenseExpiresAt", table: "Devices");
            migrationBuilder.DropColumn(name: "LicenseDays", table: "DeviceActivations");
        }
    }
}
