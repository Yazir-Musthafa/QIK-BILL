using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ExpensesCrmService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WarrantyMonths",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PartyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contacts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Contacts_Parties_CompanyId_PartyId",
                        columns: x => new { x.CompanyId, x.PartyId },
                        principalTable: "Parties",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DefaultTaxCategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                    table.UniqueConstraint("AK_ExpenseCategories_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ExpenseCategories_Accounts_CompanyId_AccountId",
                        columns: x => new { x.CompanyId, x.AccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_ExpenseCategories_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExpenseCategories_TaxCategories_CompanyId_DefaultTaxCategoryId",
                        columns: x => new { x.CompanyId, x.DefaultTaxCategoryId },
                        principalTable: "TaxCategories",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ExpenseSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AllowSelfApproval = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Leads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Organisation = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    LostReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leads", x => x.Id);
                    table.UniqueConstraint("AK_Leads_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Leads_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Leads_Parties_CompanyId_CustomerId",
                        columns: x => new { x.CompanyId, x.CustomerId },
                        principalTable: "Parties",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ItemDescription = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    IntervalMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    NextDueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceSchedules", x => x.Id);
                    table.UniqueConstraint("AK_MaintenanceSchedules_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_MaintenanceSchedules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceSchedules_Parties_CompanyId_CustomerId",
                        columns: x => new { x.CompanyId, x.CustomerId },
                        principalTable: "Parties",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_MaintenanceSchedules_Products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ServiceJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ItemDescription = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Fault = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Accessories = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Diagnosis = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Charge = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    WarrantyUntil = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    TechnicianUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EstimatedCompletion = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CancelReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeliveredOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceJobs", x => x.Id);
                    table.UniqueConstraint("AK_ServiceJobs_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ServiceJobs_BillingDocuments_CompanyId_InvoiceId",
                        columns: x => new { x.CompanyId, x.InvoiceId },
                        principalTable: "BillingDocuments",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_ServiceJobs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServiceJobs_Parties_CompanyId_CustomerId",
                        columns: x => new { x.CompanyId, x.CustomerId },
                        principalTable: "Parties",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_ServiceJobs_Products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_ServiceJobs_Warehouses_CompanyId_WarehouseId",
                        columns: x => new { x.CompanyId, x.WarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ServiceSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DefaultLabourSac = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    ReminderLeadDays = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AmountPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    TaxCategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InputTaxEligible = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PaidFromAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Method = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BranchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Department = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    NextDueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringExpenses", x => x.Id);
                    table.UniqueConstraint("AK_RecurringExpenses_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_RecurringExpenses_Accounts_CompanyId_PaidFromAccountId",
                        columns: x => new { x.CompanyId, x.PaidFromAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_RecurringExpenses_Branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_RecurringExpenses_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecurringExpenses_ExpenseCategories_CompanyId_CategoryId",
                        columns: x => new { x.CompanyId, x.CategoryId },
                        principalTable: "ExpenseCategories",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_RecurringExpenses_Parties_CompanyId_SupplierId",
                        columns: x => new { x.CompanyId, x.SupplierId },
                        principalTable: "Parties",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_RecurringExpenses_TaxCategories_CompanyId_TaxCategoryId",
                        columns: x => new { x.CompanyId, x.TaxCategoryId },
                        principalTable: "TaxCategories",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "Activities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeadId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    RecordedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Activities_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Activities_Leads_CompanyId_LeadId",
                        columns: x => new { x.CompanyId, x.LeadId },
                        principalTable: "Leads",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_Activities_Parties_CompanyId_CustomerId",
                        columns: x => new { x.CompanyId, x.CustomerId },
                        principalTable: "Parties",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "Enquiries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    LeadId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Enquiries", x => x.Id);
                    table.UniqueConstraint("AK_Enquiries_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Enquiries_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Enquiries_Leads_CompanyId_LeadId",
                        columns: x => new { x.CompanyId, x.LeadId },
                        principalTable: "Leads",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_Enquiries_Parties_CompanyId_CustomerId",
                        columns: x => new { x.CompanyId, x.CustomerId },
                        principalTable: "Parties",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_Enquiries_Products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ServiceLabour",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SacCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    QuantityMilli = table.Column<long>(type: "INTEGER", nullable: false),
                    RatePaise = table.Column<long>(type: "INTEGER", nullable: false),
                    TaxCategoryId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceLabour", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceLabour_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServiceLabour_ServiceJobs_CompanyId_JobId",
                        columns: x => new { x.CompanyId, x.JobId },
                        principalTable: "ServiceJobs",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_ServiceLabour_TaxCategories_CompanyId_TaxCategoryId",
                        columns: x => new { x.CompanyId, x.TaxCategoryId },
                        principalTable: "TaxCategories",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ServiceParts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    QuantityMilli = table.Column<long>(type: "INTEGER", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BatchNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    CostPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    Charge = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    StockDocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    IssuedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IssuedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceParts", x => x.Id);
                    table.UniqueConstraint("AK_ServiceParts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_ServiceParts_Batches_CompanyId_BatchId",
                        columns: x => new { x.CompanyId, x.BatchId },
                        principalTable: "Batches",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_ServiceParts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServiceParts_Products_CompanyId_ProductId",
                        columns: x => new { x.CompanyId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_ServiceParts_ServiceJobs_CompanyId_JobId",
                        columns: x => new { x.CompanyId, x.JobId },
                        principalTable: "ServiceJobs",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_ServiceParts_StockDocuments_CompanyId_StockDocumentId",
                        columns: x => new { x.CompanyId, x.StockDocumentId },
                        principalTable: "StockDocuments",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_ServiceParts_Vouchers_CompanyId_VoucherId",
                        columns: x => new { x.CompanyId, x.VoucherId },
                        principalTable: "Vouchers",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_ServiceParts_Warehouses_CompanyId_WarehouseId",
                        columns: x => new { x.CompanyId, x.WarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EmployeeUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EmployeeName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    PaidFromAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Method = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BranchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Department = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    IsInterState = table.Column<bool>(type: "INTEGER", nullable: false),
                    TaxablePaise = table.Column<long>(type: "INTEGER", nullable: false),
                    CgstPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    SgstPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    IgstPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    IneligibleTaxPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    RecurringExpenseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecurringDueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmittedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubmittedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DecidedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RejectionReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PostedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PostedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.UniqueConstraint("AK_Expenses_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_Expenses_Accounts_CompanyId_PaidFromAccountId",
                        columns: x => new { x.CompanyId, x.PaidFromAccountId },
                        principalTable: "Accounts",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_Expenses_Branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalTable: "Branches",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_Expenses_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Expenses_Parties_CompanyId_SupplierId",
                        columns: x => new { x.CompanyId, x.SupplierId },
                        principalTable: "Parties",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_Expenses_RecurringExpenses_CompanyId_RecurringExpenseId",
                        columns: x => new { x.CompanyId, x.RecurringExpenseId },
                        principalTable: "RecurringExpenses",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_Expenses_Vouchers_CompanyId_VoucherId",
                        columns: x => new { x.CompanyId, x.VoucherId },
                        principalTable: "Vouchers",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "FollowUps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LeadId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EnquiryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ServiceJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ScheduleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsDone = table.Column<bool>(type: "INTEGER", nullable: false),
                    DoneUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DoneBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowUps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FollowUps_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FollowUps_Enquiries_CompanyId_EnquiryId",
                        columns: x => new { x.CompanyId, x.EnquiryId },
                        principalTable: "Enquiries",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_FollowUps_Leads_CompanyId_LeadId",
                        columns: x => new { x.CompanyId, x.LeadId },
                        principalTable: "Leads",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_FollowUps_MaintenanceSchedules_CompanyId_ScheduleId",
                        columns: x => new { x.CompanyId, x.ScheduleId },
                        principalTable: "MaintenanceSchedules",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_FollowUps_Parties_CompanyId_CustomerId",
                        columns: x => new { x.CompanyId, x.CustomerId },
                        principalTable: "Parties",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_FollowUps_ServiceJobs_CompanyId_ServiceJobId",
                        columns: x => new { x.CompanyId, x.ServiceJobId },
                        principalTable: "ServiceJobs",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ServicePartReturns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StockDocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ReturnedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReturnedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicePartReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServicePartReturns_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServicePartReturns_ServiceParts_CompanyId_PartId",
                        columns: x => new { x.CompanyId, x.PartId },
                        principalTable: "ServiceParts",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_ServicePartReturns_StockDocuments_CompanyId_StockDocumentId",
                        columns: x => new { x.CompanyId, x.StockDocumentId },
                        principalTable: "StockDocuments",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_ServicePartReturns_Vouchers_CompanyId_VoucherId",
                        columns: x => new { x.CompanyId, x.VoucherId },
                        principalTable: "Vouchers",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ExpenseAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpenseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    AddedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AddedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseAttachments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExpenseAttachments_Expenses_CompanyId_ExpenseId",
                        columns: x => new { x.CompanyId, x.ExpenseId },
                        principalTable: "Expenses",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ExpenseLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpenseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AmountPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    TaxCategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TaxRatePercent = table.Column<decimal>(type: "TEXT", precision: 6, scale: 3, nullable: false),
                    CgstPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    SgstPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    IgstPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    InputTaxEligible = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseLines_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExpenseLines_ExpenseCategories_CompanyId_CategoryId",
                        columns: x => new { x.CompanyId, x.CategoryId },
                        principalTable: "ExpenseCategories",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_ExpenseLines_Expenses_CompanyId_ExpenseId",
                        columns: x => new { x.CompanyId, x.ExpenseId },
                        principalTable: "Expenses",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_ExpenseLines_TaxCategories_CompanyId_TaxCategoryId",
                        columns: x => new { x.CompanyId, x.TaxCategoryId },
                        principalTable: "TaxCategories",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ExpenseReversals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpenseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ReversalVoucherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReversedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReversedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseReversals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseReversals_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExpenseReversals_Expenses_CompanyId_ExpenseId",
                        columns: x => new { x.CompanyId, x.ExpenseId },
                        principalTable: "Expenses",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_ExpenseReversals_Vouchers_CompanyId_ReversalVoucherId",
                        columns: x => new { x.CompanyId, x.ReversalVoucherId },
                        principalTable: "Vouchers",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_CompanyId",
                table: "Activities",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_CompanyId_CustomerId",
                table: "Activities",
                columns: new[] { "CompanyId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_CompanyId_LeadId",
                table: "Activities",
                columns: new[] { "CompanyId", "LeadId" });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_CompanyId",
                table: "Contacts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_CompanyId_PartyId",
                table: "Contacts",
                columns: new[] { "CompanyId", "PartyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Enquiries_CompanyId",
                table: "Enquiries",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Enquiries_CompanyId_CustomerId",
                table: "Enquiries",
                columns: new[] { "CompanyId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Enquiries_CompanyId_LeadId",
                table: "Enquiries",
                columns: new[] { "CompanyId", "LeadId" });

            migrationBuilder.CreateIndex(
                name: "IX_Enquiries_CompanyId_ProductId",
                table: "Enquiries",
                columns: new[] { "CompanyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseAttachments_CompanyId",
                table: "ExpenseAttachments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseAttachments_CompanyId_ExpenseId",
                table: "ExpenseAttachments",
                columns: new[] { "CompanyId", "ExpenseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_CompanyId",
                table: "ExpenseCategories",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_CompanyId_AccountId",
                table: "ExpenseCategories",
                columns: new[] { "CompanyId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_CompanyId_DefaultTaxCategoryId",
                table: "ExpenseCategories",
                columns: new[] { "CompanyId", "DefaultTaxCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_CompanyId_Name",
                table: "ExpenseCategories",
                columns: new[] { "CompanyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseLines_CompanyId",
                table: "ExpenseLines",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseLines_CompanyId_CategoryId",
                table: "ExpenseLines",
                columns: new[] { "CompanyId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseLines_CompanyId_ExpenseId_LineNumber",
                table: "ExpenseLines",
                columns: new[] { "CompanyId", "ExpenseId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseLines_CompanyId_TaxCategoryId",
                table: "ExpenseLines",
                columns: new[] { "CompanyId", "TaxCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseReversals_CompanyId",
                table: "ExpenseReversals",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseReversals_CompanyId_ExpenseId",
                table: "ExpenseReversals",
                columns: new[] { "CompanyId", "ExpenseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseReversals_CompanyId_ReversalVoucherId",
                table: "ExpenseReversals",
                columns: new[] { "CompanyId", "ReversalVoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId",
                table: "Expenses",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId_BranchId",
                table: "Expenses",
                columns: new[] { "CompanyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId_Date",
                table: "Expenses",
                columns: new[] { "CompanyId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId_Number",
                table: "Expenses",
                columns: new[] { "CompanyId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId_PaidFromAccountId",
                table: "Expenses",
                columns: new[] { "CompanyId", "PaidFromAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId_RecurringExpenseId_RecurringDueDate",
                table: "Expenses",
                columns: new[] { "CompanyId", "RecurringExpenseId", "RecurringDueDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId_SupplierId",
                table: "Expenses",
                columns: new[] { "CompanyId", "SupplierId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CompanyId_VoucherId",
                table: "Expenses",
                columns: new[] { "CompanyId", "VoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseSettings_CompanyId",
                table: "ExpenseSettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_CompanyId",
                table: "FollowUps",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_CompanyId_CustomerId",
                table: "FollowUps",
                columns: new[] { "CompanyId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_CompanyId_DueDate",
                table: "FollowUps",
                columns: new[] { "CompanyId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_CompanyId_EnquiryId",
                table: "FollowUps",
                columns: new[] { "CompanyId", "EnquiryId" });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_CompanyId_LeadId",
                table: "FollowUps",
                columns: new[] { "CompanyId", "LeadId" });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_CompanyId_ScheduleId_DueDate",
                table: "FollowUps",
                columns: new[] { "CompanyId", "ScheduleId", "DueDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_CompanyId_ServiceJobId",
                table: "FollowUps",
                columns: new[] { "CompanyId", "ServiceJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_CompanyId",
                table: "Leads",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_CompanyId_CustomerId",
                table: "Leads",
                columns: new[] { "CompanyId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_CompanyId_Status",
                table: "Leads",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceSchedules_CompanyId",
                table: "MaintenanceSchedules",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceSchedules_CompanyId_CustomerId",
                table: "MaintenanceSchedules",
                columns: new[] { "CompanyId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceSchedules_CompanyId_ProductId",
                table: "MaintenanceSchedules",
                columns: new[] { "CompanyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_CompanyId",
                table: "RecurringExpenses",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_CompanyId_BranchId",
                table: "RecurringExpenses",
                columns: new[] { "CompanyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_CompanyId_CategoryId",
                table: "RecurringExpenses",
                columns: new[] { "CompanyId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_CompanyId_Name",
                table: "RecurringExpenses",
                columns: new[] { "CompanyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_CompanyId_PaidFromAccountId",
                table: "RecurringExpenses",
                columns: new[] { "CompanyId", "PaidFromAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_CompanyId_SupplierId",
                table: "RecurringExpenses",
                columns: new[] { "CompanyId", "SupplierId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_CompanyId_TaxCategoryId",
                table: "RecurringExpenses",
                columns: new[] { "CompanyId", "TaxCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobs_CompanyId",
                table: "ServiceJobs",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobs_CompanyId_CustomerId",
                table: "ServiceJobs",
                columns: new[] { "CompanyId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobs_CompanyId_InvoiceId",
                table: "ServiceJobs",
                columns: new[] { "CompanyId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobs_CompanyId_Number",
                table: "ServiceJobs",
                columns: new[] { "CompanyId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobs_CompanyId_ProductId",
                table: "ServiceJobs",
                columns: new[] { "CompanyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobs_CompanyId_SerialNumber",
                table: "ServiceJobs",
                columns: new[] { "CompanyId", "SerialNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobs_CompanyId_WarehouseId",
                table: "ServiceJobs",
                columns: new[] { "CompanyId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLabour_CompanyId",
                table: "ServiceLabour",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLabour_CompanyId_JobId",
                table: "ServiceLabour",
                columns: new[] { "CompanyId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLabour_CompanyId_TaxCategoryId",
                table: "ServiceLabour",
                columns: new[] { "CompanyId", "TaxCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicePartReturns_CompanyId",
                table: "ServicePartReturns",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePartReturns_CompanyId_PartId",
                table: "ServicePartReturns",
                columns: new[] { "CompanyId", "PartId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicePartReturns_CompanyId_StockDocumentId",
                table: "ServicePartReturns",
                columns: new[] { "CompanyId", "StockDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicePartReturns_CompanyId_VoucherId",
                table: "ServicePartReturns",
                columns: new[] { "CompanyId", "VoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceParts_CompanyId",
                table: "ServiceParts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceParts_CompanyId_BatchId",
                table: "ServiceParts",
                columns: new[] { "CompanyId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceParts_CompanyId_JobId",
                table: "ServiceParts",
                columns: new[] { "CompanyId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceParts_CompanyId_ProductId",
                table: "ServiceParts",
                columns: new[] { "CompanyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceParts_CompanyId_StockDocumentId",
                table: "ServiceParts",
                columns: new[] { "CompanyId", "StockDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceParts_CompanyId_VoucherId",
                table: "ServiceParts",
                columns: new[] { "CompanyId", "VoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceParts_CompanyId_WarehouseId",
                table: "ServiceParts",
                columns: new[] { "CompanyId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceSettings_CompanyId",
                table: "ServiceSettings",
                column: "CompanyId",
                unique: true);

            // Database-level protection matching ImmutabilityRules: posted expenses and their lines, closed service jobs,
            // and append-only records. DELETE is allowed only when the company itself is deleted.
            Trigger(migrationBuilder, "Expenses_NoUpdatePosted", "BEFORE UPDATE ON Expenses WHEN OLD.Status = 'Posted'", "Posted expenses cannot be changed.");
            Trigger(migrationBuilder, "Expenses_NoDeletePosted", "BEFORE DELETE ON Expenses WHEN OLD.Status = 'Posted' AND EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)", "Posted expenses cannot be deleted.");
            Trigger(migrationBuilder, "ExpenseLines_NoUpdateLocked", "BEFORE UPDATE ON ExpenseLines WHEN OLD.IsLocked = 1", "Posted expense lines cannot be changed.");
            Trigger(migrationBuilder, "ExpenseLines_NoDeleteLocked", "BEFORE DELETE ON ExpenseLines WHEN OLD.IsLocked = 1 AND EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)", "Posted expense lines cannot be deleted.");
            Trigger(migrationBuilder, "ExpenseAttachments_NoUpdate", "BEFORE UPDATE ON ExpenseAttachments", "Attachments cannot be changed.");
            Trigger(migrationBuilder, "ExpenseAttachments_NoDeletePosted",
                "BEFORE DELETE ON ExpenseAttachments WHEN (SELECT Status FROM Expenses WHERE Id = OLD.ExpenseId) = 'Posted' AND EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)",
                "Attachments of posted expenses cannot be deleted.");
            Trigger(migrationBuilder, "ServiceJobs_NoUpdateClosed", "BEFORE UPDATE ON ServiceJobs WHEN OLD.Status IN ('Delivered', 'Cancelled')", "Delivered or cancelled service jobs cannot be changed.");
            Trigger(migrationBuilder, "ServiceJobs_NoDeleteClosed", "BEFORE DELETE ON ServiceJobs WHEN OLD.Status IN ('Delivered', 'Cancelled') AND EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)", "Delivered or cancelled service jobs cannot be deleted.");
            foreach (var table in AppendOnly)
            {
                Trigger(migrationBuilder, table + "_NoUpdate", $"BEFORE UPDATE ON {table}", $"{table} rows cannot be changed.");
                Trigger(migrationBuilder, table + "_NoDelete", $"BEFORE DELETE ON {table} WHEN EXISTS (SELECT 1 FROM Companies WHERE Id = OLD.CompanyId)", $"{table} rows cannot be deleted.");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var name in new[] { "Expenses_NoUpdatePosted", "Expenses_NoDeletePosted", "ExpenseLines_NoUpdateLocked", "ExpenseLines_NoDeleteLocked",
                         "ExpenseAttachments_NoUpdate", "ExpenseAttachments_NoDeletePosted", "ServiceJobs_NoUpdateClosed", "ServiceJobs_NoDeleteClosed" }
                         .Concat(AppendOnly.SelectMany(t => new[] { t + "_NoUpdate", t + "_NoDelete" })))
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {name};");

            migrationBuilder.DropTable(
                name: "Activities");

            migrationBuilder.DropTable(
                name: "Contacts");

            migrationBuilder.DropTable(
                name: "ExpenseAttachments");

            migrationBuilder.DropTable(
                name: "ExpenseLines");

            migrationBuilder.DropTable(
                name: "ExpenseReversals");

            migrationBuilder.DropTable(
                name: "ExpenseSettings");

            migrationBuilder.DropTable(
                name: "FollowUps");

            migrationBuilder.DropTable(
                name: "ServiceLabour");

            migrationBuilder.DropTable(
                name: "ServicePartReturns");

            migrationBuilder.DropTable(
                name: "ServiceSettings");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "Enquiries");

            migrationBuilder.DropTable(
                name: "MaintenanceSchedules");

            migrationBuilder.DropTable(
                name: "ServiceParts");

            migrationBuilder.DropTable(
                name: "RecurringExpenses");

            migrationBuilder.DropTable(
                name: "Leads");

            migrationBuilder.DropTable(
                name: "ServiceJobs");

            migrationBuilder.DropTable(
                name: "ExpenseCategories");

            migrationBuilder.DropColumn(
                name: "WarrantyMonths",
                table: "Products");
        }
    
        private static readonly string[] AppendOnly = ["ExpenseReversals", "Activities", "ServiceParts", "ServicePartReturns"];

        private static void Trigger(MigrationBuilder migrationBuilder, string name, string when, string message) =>
            migrationBuilder.Sql($"CREATE TRIGGER {name} {when}\nBEGIN SELECT RAISE(ABORT, '{message}'); END;");
    }
}
