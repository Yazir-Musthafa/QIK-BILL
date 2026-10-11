using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ReportsCostCentresBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CostCentreId",
                table: "VoucherLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_LedgerEntries_CompanyId_Id",
                table: "LedgerEntries",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateTable(
                name: "BankClearings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClearedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StatementReference = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ClearedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClearedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankClearings", x => x.Id);
                    table.UniqueConstraint("AK_BankClearings_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_BankClearings_Accounts_CompanyId_AccountId",
                        columns: x => new { x.CompanyId, x.AccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_BankClearings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankClearings_LedgerEntries_CompanyId_LedgerEntryId",
                        columns: x => new { x.CompanyId, x.LedgerEntryId },
                        principalTable: "LedgerEntries",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "CostCentres",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostCentres", x => x.Id);
                    table.UniqueConstraint("AK_CostCentres_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_CostCentres_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgeingBuckets = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportSettings", x => x.Id);
                    table.UniqueConstraint("AK_ReportSettings_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ReportSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoucherLines_CompanyId_CostCentreId",
                table: "VoucherLines",
                columns: new[] { "CompanyId", "CostCentreId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankClearings_CompanyId",
                table: "BankClearings",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BankClearings_CompanyId_AccountId_ClearedOn",
                table: "BankClearings",
                columns: new[] { "CompanyId", "AccountId", "ClearedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_BankClearings_CompanyId_LedgerEntryId",
                table: "BankClearings",
                columns: new[] { "CompanyId", "LedgerEntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostCentres_CompanyId",
                table: "CostCentres",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCentres_CompanyId_Code",
                table: "CostCentres",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostCentres_CompanyId_Name",
                table: "CostCentres",
                columns: new[] { "CompanyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportSettings_CompanyId",
                table: "ReportSettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_VoucherLines_CostCentres_CompanyId_CostCentreId",
                table: "VoucherLines",
                columns: new[] { "CompanyId", "CostCentreId" },
                principalTable: "CostCentres",
                principalColumns: new[] { "CompanyId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VoucherLines_CostCentres_CompanyId_CostCentreId",
                table: "VoucherLines");

            migrationBuilder.DropTable(
                name: "BankClearings");

            migrationBuilder.DropTable(
                name: "CostCentres");

            migrationBuilder.DropTable(
                name: "ReportSettings");

            migrationBuilder.DropIndex(
                name: "IX_VoucherLines_CompanyId_CostCentreId",
                table: "VoucherLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_LedgerEntries_CompanyId_Id",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "CostCentreId",
                table: "VoucherLines");
        }
    }
}
