using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Portfolio.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "AssetGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetWeight = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CashBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashBalances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PriceCaches",
                columns: table => new
                {
                    SymbolCode = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    PrevClose = table.Column<decimal>(type: "TEXT", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceCaches", x => x.SymbolCode);
                });

            migrationBuilder.CreateTable(
                name: "SymbolMasters",
                columns: table => new
                {
                    SymbolCode = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    SymbolName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Market = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SymbolMasters", x => x.SymbolCode);
                });

            migrationBuilder.CreateTable(
                name: "Holdings",
                columns: table => new
                {
                    SymbolCode = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    SymbolName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    AvgPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holdings", x => x.SymbolCode);
                    table.ForeignKey(
                        name: "FK_Holdings_AssetGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "AssetGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "AssetGroups",
                columns: new[] { "Id", "Color", "Name", "SortOrder", "TargetWeight" },
                values: new object[,]
                {
                    { 1, "#23395B", "주식", 1, 0.5m },
                    { 2, "#E08A2E", "채권", 2, 0.3m },
                    { 3, "#6BB3A8", "배당", 3, 0.2m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Holdings_GroupId",
                table: "Holdings",
                column: "GroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "CashBalances");

            migrationBuilder.DropTable(
                name: "Holdings");

            migrationBuilder.DropTable(
                name: "PriceCaches");

            migrationBuilder.DropTable(
                name: "SymbolMasters");

            migrationBuilder.DropTable(
                name: "AssetGroups");
        }
    }
}
