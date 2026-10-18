using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class PurchaseEntryDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PurchaseOrderNumber",
                table: "BillingDocuments",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PurchaseOrderYear",
                table: "BillingDocuments",
                type: "TEXT",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReceivedDate",
                table: "BillingDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SupplierInvoiceDate",
                table: "BillingDocuments",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PurchaseOrderNumber",
                table: "BillingDocuments");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderYear",
                table: "BillingDocuments");

            migrationBuilder.DropColumn(
                name: "ReceivedDate",
                table: "BillingDocuments");

            migrationBuilder.DropColumn(
                name: "SupplierInvoiceDate",
                table: "BillingDocuments");
        }
    }
}
