using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using KOSHI.Infrastructure.Persistence;

#nullable disable

namespace KOSHI.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(KoshiDbContext))]
    [Migration("20261105180000_DeviceNetworkControls")]
    public partial class DeviceNetworkControls : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "VpnBlocked",
                table: "Devices",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InstallBlocked",
                table: "Devices",
                type: "boolean",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "VpnBlocked", table: "Devices");
            migrationBuilder.DropColumn(name: "InstallBlocked", table: "Devices");
        }
    }
}
