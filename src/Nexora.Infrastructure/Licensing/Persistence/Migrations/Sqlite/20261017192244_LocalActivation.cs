using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Infrastructure.Licensing.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class LocalActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "History",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_History", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Installations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    FailedAdminAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    AdminLockedUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installations", x => x.Id);
                    table.CheckConstraint("CK_ActivationStatus", "Status IN ('Pending', 'Active', 'Suspended')");
                    table.CheckConstraint("CK_SingleInstallation", "Id = 1");
                });
            migrationBuilder.Sql("CREATE TRIGGER LocalAudit_NoUpdate BEFORE UPDATE ON History BEGIN SELECT RAISE(ABORT, 'Activation history is append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER LocalAudit_NoDelete BEFORE DELETE ON History BEGIN SELECT RAISE(ABORT, 'Activation history is append-only'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "History");

            migrationBuilder.DropTable(
                name: "Installations");
        }
    }
}
