using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KOSHI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RolePermissionsAndUsageLimits : Migration
    {
        private static readonly Guid[] SystemRoleIds =
        [
            new("8f1c7a52-0d7e-4b49-9a11-1f0000000001"),
            new("8f1c7a52-0d7e-4b49-9a11-1f0000000002"),
            new("8f1c7a52-0d7e-4b49-9a11-1f0000000003"),
            new("8f1c7a52-0d7e-4b49-9a11-1f0000000004"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "Roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<List<string>>(
                name: "Permissions",
                table: "Roles",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Roles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DailyLimitMinutes",
                table: "ApplicationRules",
                type: "integer",
                nullable: true);

            foreach (var id in SystemRoleIds)
            {
                migrationBuilder.UpdateData(
                    table: "Roles",
                    keyColumn: "Id",
                    keyValue: id,
                    column: "IsSystem",
                    value: true);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsSystem", table: "Roles");
            migrationBuilder.DropColumn(name: "Permissions", table: "Roles");
            migrationBuilder.DropColumn(name: "UpdatedAt", table: "Roles");
            migrationBuilder.DropColumn(name: "DailyLimitMinutes", table: "ApplicationRules");
        }
    }
}
