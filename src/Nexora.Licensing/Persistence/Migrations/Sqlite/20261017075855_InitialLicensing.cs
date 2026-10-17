using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Licensing.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class InitialLicensing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Contact = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    InstallationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCheckedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailedAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Audit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Audit_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_TokenHash",
                table: "Accounts",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Username",
                table: "Accounts",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Audit_AccountId_AtUtc",
                table: "Audit",
                columns: new[] { "AccountId", "AtUtc" });
            migrationBuilder.Sql("CREATE TRIGGER LicenseAudit_NoUpdate BEFORE UPDATE ON Audit BEGIN SELECT RAISE(ABORT, 'Licensing history is append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER LicenseAudit_NoDelete BEFORE DELETE ON Audit BEGIN SELECT RAISE(ABORT, 'Licensing history is append-only'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Audit");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
