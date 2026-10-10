using Nexora.Domain.Audit;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;
using Nexora.Domain.Numbering;
using Nexora.Domain.Security;

namespace Nexora.Tests.Domain;

public sealed class TaxIdentifierTests
{
    // Publicly listed GSTINs; their check characters were verified independently of this implementation.
    [Theory]
    [InlineData("27AAPFU0939F1ZV")]
    [InlineData("29AAGCB7383J1Z4")]
    [InlineData("07AAGFF2194N1Z1")]
    [InlineData("33AAACH7409R1Z8")]
    [InlineData("24AAACC1206D1ZM")]
    public void Accepts_valid_gstins(string gstin) => Assert.True(TaxIdentifiers.IsValidGstin(gstin));

    [Theory]
    [InlineData("27AAPFU0939F1ZW")] // wrong check character
    [InlineData("99AAPFU0939F1ZV")] // unknown state
    [InlineData("27AAPFU0939F1Z")]  // too short
    [InlineData("27aapfu0939f1zv")] // lower case is not normalised here
    [InlineData("27AAPXU0939F1ZV")] // invalid PAN holder type
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_invalid_gstins(string? gstin) => Assert.False(TaxIdentifiers.IsValidGstin(gstin));

    [Theory]
    [InlineData("AAPFU0939F", true)]
    [InlineData("ABCPE1234F", true)]
    [InlineData("ABCXE1234F", false)]
    [InlineData("ABCPE1234", false)]
    [InlineData("12CPE1234F", false)]
    public void Validates_pan(string pan, bool valid) => Assert.Equal(valid, TaxIdentifiers.IsValidPan(pan));
}

public sealed class CompanyProfileTests
{
    private static Company NewCompany(string state = "27") =>
        new(Guid.NewGuid(), "Acme", state, new FinancialYear(2026), false, DateTime.UtcNow);

    private static CompanyProfile Profile(string state = "27", string pan = "", string gstin = "", string pincode = "", string email = "") =>
        new("Acme Traders", "Acme Traders Private Limited", state, "12 MG Road", "", "Pune", pincode, "+91 20 1234 5678", email, pan, gstin);

    [Fact]
    public void Accepts_a_complete_profile_and_derives_pan_from_gstin()
    {
        var company = NewCompany();
        company.UpdateProfile(Profile(gstin: "27aapfu0939f1zv", pincode: "411001", email: "accounts@acme.example"));

        Assert.Equal("27AAPFU0939F1ZV", company.Gstin);
        Assert.Equal("AAPFU0939F", company.Pan);
        Assert.Equal("411001", company.Pincode);
    }

    [Fact]
    public void Rejects_gstin_from_another_state() =>
        Assert.Throws<DomainException>(() => NewCompany("29").UpdateProfile(Profile(state: "29", gstin: "27AAPFU0939F1ZV")));

    [Fact]
    public void Rejects_gstin_that_does_not_contain_the_pan() =>
        Assert.Throws<DomainException>(() => NewCompany().UpdateProfile(Profile(pan: "ABCPE1234F", gstin: "27AAPFU0939F1ZV")));

    [Theory]
    [InlineData("011001", "")]
    [InlineData("41100", "")]
    [InlineData("", "not-an-email")]
    public void Rejects_bad_contact_details(string pincode, string email) =>
        Assert.Throws<DomainException>(() => NewCompany().UpdateProfile(Profile(pincode: pincode, email: email)));

    [Fact]
    public void Logo_must_be_small_png_or_jpeg()
    {
        var company = NewCompany();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
        company.SetLogo(png);
        Assert.NotNull(company.Logo);

        Assert.Throws<DomainException>(() => company.SetLogo([0x47, 0x49, 0x46, 0x38]));
        Assert.Throws<DomainException>(() => company.SetLogo(new byte[Company.LogoMaxBytes + 1]));
        company.SetLogo(null);
        Assert.Null(company.Logo);
    }
}

public sealed class RoleTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public void Administrator_always_has_every_permission()
    {
        var admin = RoleTemplates.Create(RoleTemplates.All.Single(t => t.Key == Role.AdministratorKey), CompanyId);

        Assert.All(Permissions.All, p => Assert.True(admin.Has(p.Key)));
        Assert.Throws<DomainException>(() => admin.SetPermissions([Permissions.CompanyView]));
    }

    [Fact]
    public void Rejects_unknown_permissions() =>
        Assert.Throws<DomainException>(() => new Role(Guid.NewGuid(), CompanyId, "Custom", "", null, ["sales.everything"], 0));

    [Fact]
    public void Built_in_roles_cannot_be_renamed()
    {
        var accountant = RoleTemplates.Create(RoleTemplates.All.Single(t => t.Key == "accountant"), CompanyId);
        Assert.Throws<DomainException>(() => accountant.Rename("Bookkeeper", ""));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(5.555)]
    public void Discount_limit_is_a_percentage_with_two_decimals(decimal percent) =>
        Assert.Throws<DomainException>(() => new Role(Guid.NewGuid(), CompanyId, "Custom", "", null, [], percent));

    [Fact]
    public void Read_only_role_cannot_change_anything_or_see_costs()
    {
        var readOnly = RoleTemplates.Create(RoleTemplates.All.Single(t => t.Key == "read-only"), CompanyId);

        Assert.True(readOnly.Has(Permissions.AccountsView));
        Assert.False(readOnly.Has(Permissions.InventoryViewCost));
        Assert.False(readOnly.Has(Permissions.AuditView));
        Assert.False(readOnly.Has(Permissions.DataExport));
        Assert.DoesNotContain(readOnly.Permissions, p => !p.EndsWith(".view", StringComparison.Ordinal));
    }

    [Fact]
    public void All_six_roles_from_the_brief_exist() =>
        Assert.Equal(
            ["Accountant", "Administrator", "Billing Operator", "Inventory Manager", "Read-only", "Service Staff"],
            RoleTemplates.All.Select(t => t.Name).Order());
}

public sealed class NumberingTests
{
    [Fact]
    public void Formats_with_financial_year_and_padding()
    {
        var rule = new NumberingRule(Guid.NewGuid(), Guid.NewGuid(), DocumentTypes.TaxInvoice, "INV/{FY}/", 4, true);
        Assert.Equal("INV/2026-27/0042", rule.Format(42, new FinancialYear(2026)));
        Assert.Equal(2026, rule.PeriodFor(new FinancialYear(2026)));
    }

    [Fact]
    public void Continuous_numbering_uses_a_single_period()
    {
        var rule = new NumberingRule(Guid.NewGuid(), Guid.NewGuid(), DocumentTypes.TaxInvoice, "A", 1, false);
        Assert.Equal(0, rule.PeriodFor(new FinancialYear(2026)));
        Assert.Equal("A12345", rule.Format(12345, new FinancialYear(2026)));
    }

    [Theory]
    [InlineData("has space", 4)]
    [InlineData("TOO-LONG-PREFIX-XXXXX", 4)]
    [InlineData("INV", 0)]
    [InlineData("INV", 9)]
    public void Rejects_invalid_rules(string prefix, int padding) =>
        Assert.Throws<DomainException>(() => new NumberingRule(Guid.NewGuid(), Guid.NewGuid(), DocumentTypes.TaxInvoice, prefix, padding, true));
}

public sealed class AuditChainTests
{
    private static AuditEntry Entry(string summary) =>
        new(Guid.Empty, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), null, "asha", "Updated", "Company", "1", summary, "[]");

    [Fact]
    public void Each_entry_links_to_the_previous_hash()
    {
        var first = Entry("one");
        first.Seal(1, null);
        var second = Entry("two");
        second.Seal(2, first.Hash);

        Assert.True(first.IsIntact(1, null));
        Assert.True(second.IsIntact(2, first.Hash));
        Assert.False(second.IsIntact(2, "different"));
        Assert.False(second.IsIntact(3, first.Hash));
        Assert.Equal(64, second.Hash.Length);
    }

    [Fact]
    public void Entries_cannot_be_sealed_twice()
    {
        var entry = Entry("one");
        entry.Seal(1, null);
        Assert.Throws<InvalidOperationException>(() => entry.Seal(2, null));
    }
}
