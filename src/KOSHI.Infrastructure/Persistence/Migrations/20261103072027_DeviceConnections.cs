using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOSHI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConnectedAt",
                table: "Devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisconnectedAt",
                table: "Devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeviceConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DisconnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConnectionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceConnections_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceConnections_DeviceId_ConnectedAt",
                table: "DeviceConnections",
                columns: new[] { "DeviceId", "ConnectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceConnections_DisconnectedAt",
                table: "DeviceConnections",
                column: "DisconnectedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceConnections");

            migrationBuilder.DropColumn(
                name: "ConnectedAt",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "DisconnectedAt",
                table: "Devices");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("8f1c7a52-0d7e-4b49-9a11-1f0000000001"),
                column: "Permissions",
                value: new List<string>());

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("8f1c7a52-0d7e-4b49-9a11-1f0000000002"),
                column: "Permissions",
                value: new List<string>());

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("8f1c7a52-0d7e-4b49-9a11-1f0000000003"),
                column: "Permissions",
                value: new List<string>());

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("8f1c7a52-0d7e-4b49-9a11-1f0000000004"),
                column: "Permissions",
                value: new List<string>());
        }
    }
}
