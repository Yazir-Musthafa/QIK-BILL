using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ServiceLinesCreditLimitPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "UnitId",
                table: "BillingLines",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "BillingLines",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "LineType",
                table: "BillingLines",
                type: "TEXT",
                maxLength: 12,
                nullable: false,
                defaultValue: "Goods");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                table: "BillingLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PartyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PricePaise = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerPrices_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerPrices_Parties_CompanyId_PartyId",
                        columns: x => new { x.CompanyId, x.PartyId },
                        principalTable: "Parties",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_CustomerPrices_Products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPrices_CompanyId",
                table: "CustomerPrices",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPrices_CompanyId_PartyId_ProductId",
                table: "CustomerPrices",
                columns: new[] { "CompanyId", "PartyId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPrices_CompanyId_ProductId",
                table: "CustomerPrices",
                columns: new[] { "CompanyId", "ProductId" });

            // Making ProductId and UnitId optional rebuilds BillingLines on SQLite at the end of this migration, which drops
            // its triggers. The next migration (RestoreBillingLineTriggers) recreates them after the rebuild.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerPrices");

            migrationBuilder.DropColumn(
                name: "LineType",
                table: "BillingLines");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "BillingLines");

            migrationBuilder.AlterColumn<Guid>(
                name: "UnitId",
                table: "BillingLines",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "BillingLines",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

        }
    }
}
