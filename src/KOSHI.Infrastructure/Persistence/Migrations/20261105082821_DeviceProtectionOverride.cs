using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOSHI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceProtectionOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ProtectionEnabled",
                table: "Devices",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProtectionEnabled",
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
