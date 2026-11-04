using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDividends : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TaxType",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DividendFetches",
                columns: table => new
                {
                    SymbolCode = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DividendFetches", x => x.SymbolCode);
                });

            migrationBuilder.CreateTable(
                name: "Dividends",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SymbolCode = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    RecordDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PayDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PerShare = table.Column<decimal>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dividends", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 1,
                column: "TaxType",
                value: 0);

            // 이름이 'ISA'인 계좌는 처음부터 ISA로 둔다 (사용자 요청, 2026-11-04). 설정 화면에서 바꿀 수 있다.
            migrationBuilder.Sql("UPDATE Accounts SET TaxType = 1 WHERE Name = 'ISA'");

            migrationBuilder.CreateIndex(
                name: "IX_Dividends_SymbolCode",
                table: "Dividends",
                column: "SymbolCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DividendFetches");

            migrationBuilder.DropTable(
                name: "Dividends");

            migrationBuilder.DropColumn(
                name: "TaxType",
                table: "Accounts");
        }
    }
}
