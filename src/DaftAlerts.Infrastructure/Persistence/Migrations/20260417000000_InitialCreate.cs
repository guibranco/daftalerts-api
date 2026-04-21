using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DaftAlerts.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Properties",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                DaftId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                DaftUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                Address = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                Eircode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                RoutingKey = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                PriceMonthly = table.Column<decimal>(type: "DECIMAL(10,2)", nullable: false),
                Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                Beds = table.Column<int>(type: "INTEGER", nullable: false),
                Baths = table.Column<int>(type: "INTEGER", nullable: false),
                PropertyType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                BerRating = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                MainImageUrl = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                Latitude = table.Column<double>(type: "REAL", nullable: true),
                Longitude = table.Column<double>(type: "REAL", nullable: true),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                RecycledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                RawSubject = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                RawEmailMessageId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Properties", x => x.Id));

        migrationBuilder.CreateTable(
            name: "FilterPresets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                RoutingKeysJson = table.Column<string>(type: "TEXT", nullable: false),
                MinBeds = table.Column<int>(type: "INTEGER", nullable: true),
                MaxBeds = table.Column<int>(type: "INTEGER", nullable: true),
                MinBaths = table.Column<int>(type: "INTEGER", nullable: true),
                MinPrice = table.Column<decimal>(type: "DECIMAL(10,2)", nullable: true),
                MaxPrice = table.Column<decimal>(type: "DECIMAL(10,2)", nullable: true),
                PropertyTypesJson = table.Column<string>(type: "TEXT", nullable: false),
                BerMin = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_FilterPresets", x => x.Id));

        migrationBuilder.CreateTable(
            name: "RawEmails",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                MessageId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                Subject = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                RawMimeBytes = table.Column<byte[]>(type: "BLOB", nullable: false),
                ParseStatus = table.Column<int>(type: "INTEGER", nullable: false),
                ParseError = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                LastAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                PropertyId = table.Column<Guid>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_RawEmails", x => x.Id));

        migrationBuilder.CreateTable(
            name: "GeocodeCache",
            columns: table => new
            {
                Key = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                Latitude = table.Column<double>(type: "REAL", nullable: false),
                Longitude = table.Column<double>(type: "REAL", nullable: false),
                Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_GeocodeCache", x => x.Key));

        migrationBuilder.CreateIndex("IX_Properties_DaftId", "Properties", "DaftId", unique: true);
        migrationBuilder.CreateIndex("IX_Properties_Status", "Properties", "Status");
        migrationBuilder.CreateIndex("IX_Properties_RoutingKey", "Properties", "RoutingKey");
        migrationBuilder.CreateIndex("IX_Properties_ReceivedAt", "Properties", "ReceivedAt", descending: new bool[] { true });
        migrationBuilder.CreateIndex("IX_Properties_Status_ReceivedAt", "Properties", new[] { "Status", "ReceivedAt" }, descending: new bool[] { false, true });

        migrationBuilder.CreateIndex("IX_RawEmails_MessageId", "RawEmails", "MessageId", unique: true);
        migrationBuilder.CreateIndex("IX_RawEmails_ParseStatus", "RawEmails", "ParseStatus");
        migrationBuilder.CreateIndex("IX_RawEmails_ReceivedAt", "RawEmails", "ReceivedAt");

        migrationBuilder.CreateIndex("IX_GeocodeCache_ExpiresAt", "GeocodeCache", "ExpiresAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("Properties");
        migrationBuilder.DropTable("FilterPresets");
        migrationBuilder.DropTable("RawEmails");
        migrationBuilder.DropTable("GeocodeCache");
    }
}
