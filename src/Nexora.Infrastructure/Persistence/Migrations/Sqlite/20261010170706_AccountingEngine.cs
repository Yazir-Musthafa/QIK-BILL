using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AccountingEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Nature = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SystemKey = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.UniqueConstraint("AK_Accounts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Accounts_Accounts_CompanyId_ParentId",
                        columns: x => new { x.CompanyId, x.ParentId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_Accounts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinancialYears",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartYear = table.Column<int>(type: "INTEGER", nullable: false),
                    IsClosed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClosedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialYears", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialYears_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Vouchers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FinancialYearStart = table.Column<int>(type: "INTEGER", nullable: false),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PostedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversesVoucherId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vouchers", x => x.Id);
                    table.UniqueConstraint("AK_Vouchers_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Vouchers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Vouchers_Vouchers_CompanyId_ReversesVoucherId",
                        columns: x => new { x.CompanyId, x.ReversesVoucherId },
                        principalTable: "Vouchers",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FinancialYearStart = table.Column<int>(type: "INTEGER", nullable: false),
                    DebitPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    CreditPaise = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Accounts_CompanyId_AccountId",
                        columns: x => new { x.CompanyId, x.AccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Vouchers_CompanyId_VoucherId",
                        columns: x => new { x.CompanyId, x.VoucherId },
                        principalTable: "Vouchers",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "VoucherLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DebitPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    CreditPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoucherLines_Accounts_CompanyId_AccountId",
                        columns: x => new { x.CompanyId, x.AccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_VoucherLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VoucherLines_Vouchers_CompanyId_VoucherId",
                        columns: x => new { x.CompanyId, x.VoucherId },
                        principalTable: "Vouchers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_CompanyId",
                table: "Accounts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_CompanyId_Code",
                table: "Accounts",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_CompanyId_Name",
                table: "Accounts",
                columns: new[] { "CompanyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_CompanyId_ParentId",
                table: "Accounts",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_CompanyId_SystemKey",
                table: "Accounts",
                columns: new[] { "CompanyId", "SystemKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialYears_CompanyId",
                table: "FinancialYears",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialYears_CompanyId_StartYear",
                table: "FinancialYears",
                columns: new[] { "CompanyId", "StartYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_CompanyId",
                table: "LedgerEntries",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_CompanyId_AccountId_Date",
                table: "LedgerEntries",
                columns: new[] { "CompanyId", "AccountId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_CompanyId_Date",
                table: "LedgerEntries",
                columns: new[] { "CompanyId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_CompanyId_VoucherId_LineNumber",
                table: "LedgerEntries",
                columns: new[] { "CompanyId", "VoucherId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_CompanyId",
                table: "VoucherLines",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_CompanyId_AccountId",
                table: "VoucherLines",
                columns: new[] { "CompanyId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_CompanyId_VoucherId_LineNumber",
                table: "VoucherLines",
                columns: new[] { "CompanyId", "VoucherId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_CompanyId",
                table: "Vouchers",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_CompanyId_Date",
                table: "Vouchers",
                columns: new[] { "CompanyId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_CompanyId_ReversesVoucherId",
                table: "Vouchers",
                columns: new[] { "CompanyId", "ReversesVoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_CompanyId_Type_Number",
                table: "Vouchers",
                columns: new[] { "CompanyId", "Type", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_ReversesVoucherId",
                table: "Vouchers",
                column: "ReversesVoucherId",
                unique: true);

            // Database-level immutability for posted accounting records (SQLite-specific, like this migration).
            // Direct UPDATEs are always refused. DELETEs are refused while the company exists, so removing a
            // demonstration company (which cascades from Companies) still works.
            migrationBuilder.Sql(@"CREATE TRIGGER LedgerEntries_NoUpdate BEFORE UPDATE ON LedgerEntries
BEGIN SELECT RAISE(ABORT, 'Posted ledger entries cannot be changed.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER LedgerEntries_NoDelete BEFORE DELETE ON LedgerEntries
WHEN EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)
BEGIN SELECT RAISE(ABORT, 'Posted ledger entries cannot be deleted.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER Vouchers_NoUpdatePosted BEFORE UPDATE ON Vouchers WHEN OLD.Status = 'Posted'
BEGIN SELECT RAISE(ABORT, 'Posted vouchers cannot be changed.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER Vouchers_NoDeletePosted BEFORE DELETE ON Vouchers
WHEN OLD.Status = 'Posted' AND EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)
BEGIN SELECT RAISE(ABORT, 'Posted vouchers cannot be deleted.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER VoucherLines_NoUpdateLocked BEFORE UPDATE ON VoucherLines WHEN OLD.IsLocked = 1
BEGIN SELECT RAISE(ABORT, 'Posted voucher lines cannot be changed.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER VoucherLines_NoDeleteLocked BEFORE DELETE ON VoucherLines
WHEN OLD.IsLocked = 1 AND EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)
BEGIN SELECT RAISE(ABORT, 'Posted voucher lines cannot be deleted.'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var trigger in new[] { "LedgerEntries_NoUpdate", "LedgerEntries_NoDelete", "Vouchers_NoUpdatePosted",
                         "Vouchers_NoDeletePosted", "VoucherLines_NoUpdateLocked", "VoucherLines_NoDeleteLocked" })
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {trigger};");
            }

            migrationBuilder.DropTable(
                name: "FinancialYears");

            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "VoucherLines");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "Vouchers");
        }
    }
}
