using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Printing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LabelLayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    PageWidthMm = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    PageHeightMm = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    LabelWidthMm = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    LabelHeightMm = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    Columns = table.Column<int>(type: "INTEGER", nullable: false),
                    Rows = table.Column<int>(type: "INTEGER", nullable: false),
                    MarginLeftMm = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    MarginTopMm = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    HorizontalGapMm = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    VerticalGapMm = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    ShowName = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowSku = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowPrice = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowMrp = table.Column<bool>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelLayouts", x => x.Id);
                    table.UniqueConstraint("AK_LabelLayouts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_LabelLayouts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrinterProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PrinterName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Paper = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Orientation = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    MarginTopMm = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    MarginRightMm = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    MarginBottomMm = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    MarginLeftMm = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    ScalePercent = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    FontSize = table.Column<decimal>(type: "TEXT", precision: 7, scale: 2, nullable: false),
                    Copies = table.Column<int>(type: "INTEGER", nullable: false),
                    UseEscPos = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrinterProfiles_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrinterTests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrinterName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Paper = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    RequestedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConfirmedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConfirmedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterTests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrinterTests_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrintRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DocumentNumber = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Output = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    PrinterName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Copies = table.Column<int>(type: "INTEGER", nullable: false),
                    PrintNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    MarkedDuplicate = table.Column<bool>(type: "INTEGER", nullable: false),
                    PrintedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PrintedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintRecords_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrintSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MarkReprintsDuplicate = table.Column<bool>(type: "INTEGER", nullable: false),
                    LabelQrFormat = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    InvoiceQrFormat = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FooterNote = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LabelLayouts_CompanyId",
                table: "LabelLayouts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_LabelLayouts_CompanyId_Name",
                table: "LabelLayouts",
                columns: new[] { "CompanyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrinterProfiles_CompanyId",
                table: "PrinterProfiles",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterProfiles_CompanyId_Purpose",
                table: "PrinterProfiles",
                columns: new[] { "CompanyId", "Purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrinterTests_CompanyId",
                table: "PrinterTests",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterTests_CompanyId_RequestedUtc",
                table: "PrinterTests",
                columns: new[] { "CompanyId", "RequestedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PrintRecords_CompanyId",
                table: "PrintRecords",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintRecords_CompanyId_PrintedUtc",
                table: "PrintRecords",
                columns: new[] { "CompanyId", "PrintedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PrintRecords_CompanyId_SourceType_SourceId",
                table: "PrintRecords",
                columns: new[] { "CompanyId", "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PrintSettings_CompanyId",
                table: "PrintSettings",
                column: "CompanyId",
                unique: true);

            // Print history is append-only, like the ledgers: UPDATE is always refused, DELETE only while the company exists.
            migrationBuilder.Sql(@"CREATE TRIGGER PrintRecords_NoUpdate BEFORE UPDATE ON PrintRecords
BEGIN SELECT RAISE(ABORT, 'Print records cannot be changed.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER PrintRecords_NoDelete BEFORE DELETE ON PrintRecords
WHEN EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)
BEGIN SELECT RAISE(ABORT, 'Print records cannot be deleted.'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS PrintRecords_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS PrintRecords_NoDelete;");

            migrationBuilder.DropTable(
                name: "LabelLayouts");

            migrationBuilder.DropTable(
                name: "PrinterProfiles");

            migrationBuilder.DropTable(
                name: "PrinterTests");

            migrationBuilder.DropTable(
                name: "PrintRecords");

            migrationBuilder.DropTable(
                name: "PrintSettings");
        }
    }
}
