using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <summary>
    /// ServiceLinesCreditLimitPrices rebuilt BillingLines (SQLite cannot make a column optional in place), which dropped
    /// the triggers that keep posted lines immutable. They are recreated here, after the rebuild has run.
    /// </summary>
    public partial class RestoreBillingLineTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS BillingLines_NoUpdateLocked;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS BillingLines_NoDeleteLocked;");
            migrationBuilder.Sql(@"CREATE TRIGGER BillingLines_NoUpdateLocked BEFORE UPDATE ON BillingLines WHEN OLD.IsLocked = 1
BEGIN SELECT RAISE(ABORT, 'Posted document lines cannot be changed.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER BillingLines_NoDeleteLocked BEFORE DELETE ON BillingLines
WHEN OLD.IsLocked = 1 AND EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)
BEGIN SELECT RAISE(ABORT, 'Posted document lines cannot be deleted.'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the triggers belong to the Billing migration's design and stay.
        }
    }
}
