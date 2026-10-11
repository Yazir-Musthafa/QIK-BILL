using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <summary>
    /// ReportsCostCentresBank rebuilt LedgerEntries (a unique key for bank clearings) and VoucherLines (the cost centre
    /// foreign key); SQLite rebuilds drop triggers, so the immutability triggers are recreated here, after the rebuild.
    /// </summary>
    public partial class RestoreLedgerTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var trigger in new[] { "LedgerEntries_NoUpdate", "LedgerEntries_NoDelete", "VoucherLines_NoUpdateLocked", "VoucherLines_NoDeleteLocked" })
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {trigger};");
            migrationBuilder.Sql(@"CREATE TRIGGER LedgerEntries_NoUpdate BEFORE UPDATE ON LedgerEntries
BEGIN SELECT RAISE(ABORT, 'Posted ledger entries cannot be changed.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER LedgerEntries_NoDelete BEFORE DELETE ON LedgerEntries
WHEN EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)
BEGIN SELECT RAISE(ABORT, 'Posted ledger entries cannot be deleted.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER VoucherLines_NoUpdateLocked BEFORE UPDATE ON VoucherLines WHEN OLD.IsLocked = 1
BEGIN SELECT RAISE(ABORT, 'Posted voucher lines cannot be changed.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER VoucherLines_NoDeleteLocked BEFORE DELETE ON VoucherLines
WHEN OLD.IsLocked = 1 AND EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)
BEGIN SELECT RAISE(ABORT, 'Posted voucher lines cannot be deleted.'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the triggers belong to the AccountingEngine migration's design and stay.
        }
    }
}
