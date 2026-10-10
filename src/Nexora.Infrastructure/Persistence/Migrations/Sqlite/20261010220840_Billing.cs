using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Billing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoundToRupee = table.Column<bool>(type: "INTEGER", nullable: false),
                    PricesIncludeTax = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Parties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Gstin = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    StateCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    City = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Pincode = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    CreditLimitPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parties", x => x.Id);
                    table.UniqueConstraint("AK_Parties_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Parties_Accounts_CompanyId_AccountId",
                        columns: x => new { x.CompanyId, x.AccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_Parties_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PartyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChargeSource = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ChargeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreditSource = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CreditId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AmountPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Allocations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Allocations_Parties_CompanyId_PartyId",
                        columns: x => new { x.CompanyId, x.PartyId },
                        principalTable: "Parties",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "BillingDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PartyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PartyName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PartyGstin = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    PlaceOfSupply = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    IsInterState = table.Column<bool>(type: "INTEGER", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CashAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalDocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    PricesIncludeTax = table.Column<bool>(type: "INTEGER", nullable: false),
                    RoundToRupee = table.Column<bool>(type: "INTEGER", nullable: false),
                    GrossAmountPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    DiscountAmountPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    TaxableAmountPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    CgstPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    SgstPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    IgstPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    RoundOffPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    CostOfGoodsPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StockDocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PostedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingDocuments", x => x.Id);
                    table.UniqueConstraint("AK_BillingDocuments_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_BillingDocuments_Accounts_CompanyId_CashAccountId",
                        columns: x => new { x.CompanyId, x.CashAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_BillingDocuments_BillingDocuments_CompanyId_OriginalDocumentId",
                        columns: x => new { x.CompanyId, x.OriginalDocumentId },
                        principalTable: "BillingDocuments",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_BillingDocuments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillingDocuments_Parties_CompanyId_PartyId",
                        columns: x => new { x.CompanyId, x.PartyId },
                        principalTable: "Parties",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_BillingDocuments_StockDocuments_CompanyId_StockDocumentId",
                        columns: x => new { x.CompanyId, x.StockDocumentId },
                        principalTable: "StockDocuments",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_BillingDocuments_Vouchers_CompanyId_VoucherId",
                        columns: x => new { x.CompanyId, x.VoucherId },
                        principalTable: "Vouchers",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_BillingDocuments_Warehouses_CompanyId_WarehouseId",
                        columns: x => new { x.CompanyId, x.WarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "Settlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PartyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AmountPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PostedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settlements", x => x.Id);
                    table.UniqueConstraint("AK_Settlements_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Settlements_Accounts_CompanyId_AccountId",
                        columns: x => new { x.CompanyId, x.AccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_Settlements_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Settlements_Parties_CompanyId_PartyId",
                        columns: x => new { x.CompanyId, x.PartyId },
                        principalTable: "Parties",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_Settlements_Vouchers_CompanyId_VoucherId",
                        columns: x => new { x.CompanyId, x.VoucherId },
                        principalTable: "Vouchers",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "BillingCancellations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ReversalVoucherId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StockReversalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CancelledUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CancelledBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCancellations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingCancellations_BillingDocuments_CompanyId_DocumentId",
                        columns: x => new { x.CompanyId, x.DocumentId },
                        principalTable: "BillingDocuments",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_BillingCancellations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillingCancellations_StockDocuments_CompanyId_StockReversalId",
                        columns: x => new { x.CompanyId, x.StockReversalId },
                        principalTable: "StockDocuments",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_BillingCancellations_Vouchers_CompanyId_ReversalVoucherId",
                        columns: x => new { x.CompanyId, x.ReversalVoucherId },
                        principalTable: "Vouchers",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "BillingLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    HsnCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    UnitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuantityMilli = table.Column<long>(type: "INTEGER", nullable: false),
                    BaseQuantityMilli = table.Column<long>(type: "INTEGER", nullable: false),
                    RatePaise = table.Column<long>(type: "INTEGER", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    GrossAmountPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    DiscountAmountPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    TaxableAmountPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    TaxCategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TaxRatePercent = table.Column<decimal>(type: "TEXT", precision: 6, scale: 3, nullable: false),
                    CgstPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    SgstPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    IgstPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BatchNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    OriginalLineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CostValuePaise = table.Column<long>(type: "INTEGER", nullable: false),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingLines", x => x.Id);
                    table.UniqueConstraint("AK_BillingLines_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_BillingLines_Batches_CompanyId_BatchId",
                        columns: x => new { x.CompanyId, x.BatchId },
                        principalTable: "Batches",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_BillingLines_BillingDocuments_CompanyId_DocumentId",
                        columns: x => new { x.CompanyId, x.DocumentId },
                        principalTable: "BillingDocuments",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_BillingLines_BillingLines_CompanyId_OriginalLineId",
                        columns: x => new { x.CompanyId, x.OriginalLineId },
                        principalTable: "BillingLines",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_BillingLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillingLines_Products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_BillingLines_TaxCategories_CompanyId_TaxCategoryId",
                        columns: x => new { x.CompanyId, x.TaxCategoryId },
                        principalTable: "TaxCategories",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_BillingLines_Units_CompanyId_UnitId",
                        columns: x => new { x.CompanyId, x.UnitId },
                        principalTable: "Units",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "SettlementReversals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SettlementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ReversalVoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReversedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReversedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementReversals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettlementReversals_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SettlementReversals_Settlements_CompanyId_SettlementId",
                        columns: x => new { x.CompanyId, x.SettlementId },
                        principalTable: "Settlements",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_SettlementReversals_Vouchers_CompanyId_ReversalVoucherId",
                        columns: x => new { x.CompanyId, x.ReversalVoucherId },
                        principalTable: "Vouchers",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_CompanyId",
                table: "Allocations",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_CompanyId_ChargeId",
                table: "Allocations",
                columns: new[] { "CompanyId", "ChargeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_CompanyId_CreditId",
                table: "Allocations",
                columns: new[] { "CompanyId", "CreditId" });

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_CompanyId_PartyId",
                table: "Allocations",
                columns: new[] { "CompanyId", "PartyId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCancellations_CompanyId",
                table: "BillingCancellations",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingCancellations_CompanyId_DocumentId",
                table: "BillingCancellations",
                columns: new[] { "CompanyId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingCancellations_CompanyId_ReversalVoucherId",
                table: "BillingCancellations",
                columns: new[] { "CompanyId", "ReversalVoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCancellations_CompanyId_StockReversalId",
                table: "BillingCancellations",
                columns: new[] { "CompanyId", "StockReversalId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_CompanyId",
                table: "BillingDocuments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_CompanyId_CashAccountId",
                table: "BillingDocuments",
                columns: new[] { "CompanyId", "CashAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_CompanyId_Kind_Date",
                table: "BillingDocuments",
                columns: new[] { "CompanyId", "Kind", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_CompanyId_Kind_Number",
                table: "BillingDocuments",
                columns: new[] { "CompanyId", "Kind", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_CompanyId_OriginalDocumentId",
                table: "BillingDocuments",
                columns: new[] { "CompanyId", "OriginalDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_CompanyId_PartyId",
                table: "BillingDocuments",
                columns: new[] { "CompanyId", "PartyId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_CompanyId_StockDocumentId",
                table: "BillingDocuments",
                columns: new[] { "CompanyId", "StockDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_CompanyId_VoucherId",
                table: "BillingDocuments",
                columns: new[] { "CompanyId", "VoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingDocuments_CompanyId_WarehouseId",
                table: "BillingDocuments",
                columns: new[] { "CompanyId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingLines_CompanyId",
                table: "BillingLines",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingLines_CompanyId_BatchId",
                table: "BillingLines",
                columns: new[] { "CompanyId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingLines_CompanyId_DocumentId_LineNumber",
                table: "BillingLines",
                columns: new[] { "CompanyId", "DocumentId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingLines_CompanyId_OriginalLineId",
                table: "BillingLines",
                columns: new[] { "CompanyId", "OriginalLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingLines_CompanyId_ProductId",
                table: "BillingLines",
                columns: new[] { "CompanyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingLines_CompanyId_TaxCategoryId",
                table: "BillingLines",
                columns: new[] { "CompanyId", "TaxCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingLines_CompanyId_UnitId",
                table: "BillingLines",
                columns: new[] { "CompanyId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingSettings_CompanyId",
                table: "BillingSettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parties_AccountId",
                table: "Parties",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parties_CompanyId",
                table: "Parties",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Parties_CompanyId_AccountId",
                table: "Parties",
                columns: new[] { "CompanyId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Parties_CompanyId_Kind_Name",
                table: "Parties",
                columns: new[] { "CompanyId", "Kind", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettlementReversals_CompanyId",
                table: "SettlementReversals",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementReversals_CompanyId_ReversalVoucherId",
                table: "SettlementReversals",
                columns: new[] { "CompanyId", "ReversalVoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_SettlementReversals_CompanyId_SettlementId",
                table: "SettlementReversals",
                columns: new[] { "CompanyId", "SettlementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_CompanyId",
                table: "Settlements",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_CompanyId_AccountId",
                table: "Settlements",
                columns: new[] { "CompanyId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_CompanyId_Kind_Number",
                table: "Settlements",
                columns: new[] { "CompanyId", "Kind", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_CompanyId_PartyId",
                table: "Settlements",
                columns: new[] { "CompanyId", "PartyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_CompanyId_VoucherId",
                table: "Settlements",
                columns: new[] { "CompanyId", "VoucherId" });

            // Posted billing documents and their lines are immutable, like posted vouchers; settlements, allocations,
            // cancellations and reversals are append-only. DELETEs are allowed only when the company itself is deleted.
            migrationBuilder.Sql(@"CREATE TRIGGER BillingDocuments_NoUpdatePosted BEFORE UPDATE ON BillingDocuments WHEN OLD.Status = 'Posted'
BEGIN SELECT RAISE(ABORT, 'Posted sales and purchase documents cannot be changed.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER BillingDocuments_NoDeletePosted BEFORE DELETE ON BillingDocuments
WHEN OLD.Status = 'Posted' AND EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)
BEGIN SELECT RAISE(ABORT, 'Posted sales and purchase documents cannot be deleted.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER BillingLines_NoUpdateLocked BEFORE UPDATE ON BillingLines WHEN OLD.IsLocked = 1
BEGIN SELECT RAISE(ABORT, 'Posted document lines cannot be changed.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER BillingLines_NoDeleteLocked BEFORE DELETE ON BillingLines
WHEN OLD.IsLocked = 1 AND EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)
BEGIN SELECT RAISE(ABORT, 'Posted document lines cannot be deleted.'); END;");
            foreach (var table in AppendOnlyTables)
            {
                migrationBuilder.Sql($@"CREATE TRIGGER {table}_NoUpdate BEFORE UPDATE ON {table}
BEGIN SELECT RAISE(ABORT, '{table} rows cannot be changed.'); END;");
                migrationBuilder.Sql($@"CREATE TRIGGER {table}_NoDelete BEFORE DELETE ON {table}
WHEN EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)
BEGIN SELECT RAISE(ABORT, '{table} rows cannot be deleted.'); END;");
            }
        }

        private static readonly string[] AppendOnlyTables = ["Settlements", "SettlementReversals", "Allocations", "BillingCancellations"];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var trigger in new[] { "BillingDocuments_NoUpdatePosted", "BillingDocuments_NoDeletePosted", "BillingLines_NoUpdateLocked", "BillingLines_NoDeleteLocked" }
                         .Concat(AppendOnlyTables.SelectMany(t => new[] { t + "_NoUpdate", t + "_NoDelete" })))
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {trigger};");
            }

            migrationBuilder.DropTable(
                name: "Allocations");

            migrationBuilder.DropTable(
                name: "BillingCancellations");

            migrationBuilder.DropTable(
                name: "BillingLines");

            migrationBuilder.DropTable(
                name: "BillingSettings");

            migrationBuilder.DropTable(
                name: "SettlementReversals");

            migrationBuilder.DropTable(
                name: "BillingDocuments");

            migrationBuilder.DropTable(
                name: "Settlements");

            migrationBuilder.DropTable(
                name: "Parties");
        }
    }
}
