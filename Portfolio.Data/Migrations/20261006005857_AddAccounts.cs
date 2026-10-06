using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Holdings",
                table: "Holdings");

            migrationBuilder.AddColumn<int>(
                name: "AccountId",
                table: "Holdings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);   // 기존 데이터는 모두 처음 계좌(Id 1)로 옮긴다

            migrationBuilder.AddColumn<int>(
                name: "AccountId",
                table: "CashBalances",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);   // 기존 데이터는 모두 처음 계좌(Id 1)로 옮긴다

            migrationBuilder.AddColumn<int>(
                name: "AccountId",
                table: "AssetGroups",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);   // 기존 데이터는 모두 처음 계좌(Id 1)로 옮긴다

            migrationBuilder.AddPrimaryKey(
                name: "PK_Holdings",
                table: "Holdings",
                columns: new[] { "AccountId", "SymbolCode" });

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Accounts",
                columns: new[] { "Id", "Name", "SortOrder" },
                values: new object[] { 1, "기본 계좌", 1 });

            // 이 마이그레이션 전에 넣어 둔 보유 종목이 있으면 그 계좌 이름을 ISA로 한다 (사용자 요청, 2026-10-06).
            // 새로 설치한 빈 DB는 '기본 계좌'로 시작한다.
            migrationBuilder.Sql("UPDATE Accounts SET Name = 'ISA' WHERE Id = 1 AND EXISTS (SELECT 1 FROM Holdings)");

            migrationBuilder.UpdateData(
                table: "AssetGroups",
                keyColumn: "Id",
                keyValue: 1,
                column: "AccountId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "AssetGroups",
                keyColumn: "Id",
                keyValue: 2,
                column: "AccountId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "AssetGroups",
                keyColumn: "Id",
                keyValue: 3,
                column: "AccountId",
                value: 1);

            migrationBuilder.CreateIndex(
                name: "IX_CashBalances_AccountId",
                table: "CashBalances",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetGroups_AccountId",
                table: "AssetGroups",
                column: "AccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_AssetGroups_Accounts_AccountId",
                table: "AssetGroups",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CashBalances_Accounts_AccountId",
                table: "CashBalances",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Holdings_Accounts_AccountId",
                table: "Holdings",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssetGroups_Accounts_AccountId",
                table: "AssetGroups");

            migrationBuilder.DropForeignKey(
                name: "FK_CashBalances_Accounts_AccountId",
                table: "CashBalances");

            migrationBuilder.DropForeignKey(
                name: "FK_Holdings_Accounts_AccountId",
                table: "Holdings");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Holdings",
                table: "Holdings");

            migrationBuilder.DropIndex(
                name: "IX_CashBalances_AccountId",
                table: "CashBalances");

            migrationBuilder.DropIndex(
                name: "IX_AssetGroups_AccountId",
                table: "AssetGroups");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Holdings");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "CashBalances");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "AssetGroups");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Holdings",
                table: "Holdings",
                column: "SymbolCode");
        }
    }
}
