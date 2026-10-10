using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                table: "Vouchers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "Vouchers",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventorySettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CostingMethod = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AllowNegativeStock = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventorySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventorySettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToWarehouseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CounterAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversesDocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PostedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PostedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockDocuments", x => x.Id);
                    table.UniqueConstraint("AK_StockDocuments_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_StockDocuments_Accounts_CompanyId_CounterAccountId",
                        columns: x => new { x.CompanyId, x.CounterAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockDocuments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockDocuments_StockDocuments_CompanyId_ReversesDocumentId",
                        columns: x => new { x.CompanyId, x.ReversesDocumentId },
                        principalTable: "StockDocuments",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockDocuments_Vouchers_CompanyId_VoucherId",
                        columns: x => new { x.CompanyId, x.VoucherId },
                        principalTable: "Vouchers",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockDocuments_Warehouses_CompanyId_ToWarehouseId",
                        columns: x => new { x.CompanyId, x.ToWarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockDocuments_Warehouses_CompanyId_WarehouseId",
                        columns: x => new { x.CompanyId, x.WarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "TaxCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ReferenceRatePercent = table.Column<decimal>(type: "TEXT", precision: 6, scale: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxCategories", x => x.Id);
                    table.UniqueConstraint("AK_TaxCategories_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_TaxCategories_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DecimalPlaces = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Units", x => x.Id);
                    table.UniqueConstraint("AK_Units_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Units_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sku = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Barcode = table.Column<string>(type: "TEXT", maxLength: 48, nullable: true),
                    QrCode = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    HsnCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    UnitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaxCategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PurchasePricePaise = table.Column<long>(type: "INTEGER", nullable: false),
                    SellingPricePaise = table.Column<long>(type: "INTEGER", nullable: false),
                    MrpPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    ReorderLevelMilli = table.Column<long>(type: "INTEGER", nullable: false),
                    Tracking = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    TracksExpiry = table.Column<bool>(type: "INTEGER", nullable: false),
                    ParentProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VariantSize = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    VariantColour = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    VariantModel = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.UniqueConstraint("AK_Products_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Products_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Products_Products_CompanyId_ParentProductId",
                        columns: x => new { x.CompanyId, x.ParentProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_Products_TaxCategories_CompanyId_TaxCategoryId",
                        columns: x => new { x.CompanyId, x.TaxCategoryId },
                        principalTable: "TaxCategories",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_Products_Units_CompanyId_UnitId",
                        columns: x => new { x.CompanyId, x.UnitId },
                        principalTable: "Units",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "Batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ManufacturedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Batches", x => x.Id);
                    table.UniqueConstraint("AK_Batches_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Batches_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Batches_Products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "UnitConversions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UnitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Factor = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitConversions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitConversions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UnitConversions_Products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_UnitConversions_Units_CompanyId_UnitId",
                        columns: x => new { x.CompanyId, x.UnitId },
                        principalTable: "Units",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    QuantityMilli = table.Column<long>(type: "INTEGER", nullable: false),
                    ValuePaise = table.Column<long>(type: "INTEGER", nullable: false),
                    IsInternal = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnteredUnitId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EnteredQuantityMilli = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMovements_Batches_CompanyId_BatchId",
                        columns: x => new { x.CompanyId, x.BatchId },
                        principalTable: "Batches",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockMovements_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockMovements_Products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockMovements_StockDocuments_CompanyId_DocumentId",
                        columns: x => new { x.CompanyId, x.DocumentId },
                        principalTable: "StockDocuments",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockMovements_Units_CompanyId_EnteredUnitId",
                        columns: x => new { x.CompanyId, x.EnteredUnitId },
                        principalTable: "Units",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_StockMovements_Warehouses_CompanyId_WarehouseId",
                        columns: x => new { x.CompanyId, x.WarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Batches_CompanyId",
                table: "Batches",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_CompanyId_ProductId_Number",
                table: "Batches",
                columns: new[] { "CompanyId", "ProductId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventorySettings_CompanyId",
                table: "InventorySettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_CompanyId",
                table: "Products",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CompanyId_Barcode",
                table: "Products",
                columns: new[] { "CompanyId", "Barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_CompanyId_ParentProductId",
                table: "Products",
                columns: new[] { "CompanyId", "ParentProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CompanyId_QrCode",
                table: "Products",
                columns: new[] { "CompanyId", "QrCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_CompanyId_Sku",
                table: "Products",
                columns: new[] { "CompanyId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_CompanyId_TaxCategoryId",
                table: "Products",
                columns: new[] { "CompanyId", "TaxCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CompanyId_UnitId",
                table: "Products",
                columns: new[] { "CompanyId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_CompanyId",
                table: "StockDocuments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_CompanyId_CounterAccountId",
                table: "StockDocuments",
                columns: new[] { "CompanyId", "CounterAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_CompanyId_Number",
                table: "StockDocuments",
                columns: new[] { "CompanyId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_CompanyId_ReversesDocumentId",
                table: "StockDocuments",
                columns: new[] { "CompanyId", "ReversesDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_CompanyId_ToWarehouseId",
                table: "StockDocuments",
                columns: new[] { "CompanyId", "ToWarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_CompanyId_VoucherId",
                table: "StockDocuments",
                columns: new[] { "CompanyId", "VoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_CompanyId_WarehouseId",
                table: "StockDocuments",
                columns: new[] { "CompanyId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocuments_ReversesDocumentId",
                table: "StockDocuments",
                column: "ReversesDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CompanyId",
                table: "StockMovements",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CompanyId_BatchId",
                table: "StockMovements",
                columns: new[] { "CompanyId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CompanyId_Date",
                table: "StockMovements",
                columns: new[] { "CompanyId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CompanyId_DocumentId",
                table: "StockMovements",
                columns: new[] { "CompanyId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CompanyId_EnteredUnitId",
                table: "StockMovements",
                columns: new[] { "CompanyId", "EnteredUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CompanyId_ProductId_Sequence",
                table: "StockMovements",
                columns: new[] { "CompanyId", "ProductId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CompanyId_Sequence",
                table: "StockMovements",
                columns: new[] { "CompanyId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CompanyId_WarehouseId",
                table: "StockMovements",
                columns: new[] { "CompanyId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxCategories_CompanyId",
                table: "TaxCategories",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxCategories_CompanyId_Name",
                table: "TaxCategories",
                columns: new[] { "CompanyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitConversions_CompanyId",
                table: "UnitConversions",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitConversions_CompanyId_ProductId_UnitId",
                table: "UnitConversions",
                columns: new[] { "CompanyId", "ProductId", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitConversions_CompanyId_UnitId",
                table: "UnitConversions",
                columns: new[] { "CompanyId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_Units_CompanyId",
                table: "Units",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Units_CompanyId_Code",
                table: "Units",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            // The stock ledger is append-only, like the accounting ledger. Direct UPDATEs are always refused;
            // DELETEs only while the company exists (deleting a demonstration company cascades from Companies).
            migrationBuilder.Sql(@"CREATE TRIGGER StockMovements_NoUpdate BEFORE UPDATE ON StockMovements
BEGIN SELECT RAISE(ABORT, 'Stock movements cannot be changed.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER StockMovements_NoDelete BEFORE DELETE ON StockMovements
WHEN EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)
BEGIN SELECT RAISE(ABORT, 'Stock movements cannot be deleted.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER StockDocuments_NoUpdate BEFORE UPDATE ON StockDocuments
BEGIN SELECT RAISE(ABORT, 'Posted stock documents cannot be changed.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER StockDocuments_NoDelete BEFORE DELETE ON StockDocuments
WHEN EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)
BEGIN SELECT RAISE(ABORT, 'Posted stock documents cannot be deleted.'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var trigger in new[] { "StockMovements_NoUpdate", "StockMovements_NoDelete", "StockDocuments_NoUpdate", "StockDocuments_NoDelete" })
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {trigger};");
            }

            migrationBuilder.DropTable(
                name: "InventorySettings");

            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropTable(
                name: "UnitConversions");

            migrationBuilder.DropTable(
                name: "Batches");

            migrationBuilder.DropTable(
                name: "StockDocuments");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "TaxCategories");

            migrationBuilder.DropTable(
                name: "Units");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "Vouchers");
        }
    }
}
