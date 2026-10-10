using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexora.Domain.Companies;
using Nexora.Domain.Settings;
using Nexora.Domain.Users;

namespace Nexora.Infrastructure.Persistence.Configurations;

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("Companies");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(Company.NameMaxLength).IsRequired();
        builder.Property(c => c.StateCode).HasMaxLength(2).IsRequired();
        builder.Property(c => c.CreatedUtc).HasConversion(UtcConverter.Instance);
        builder.Ignore(c => c.FirstFinancialYear);
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Username).HasMaxLength(Username.MaxLength).IsRequired();
        builder.Property(u => u.NormalizedUsername).HasMaxLength(Username.MaxLength).IsRequired();
        builder.HasIndex(u => u.NormalizedUsername).IsUnique();
        builder.Property(u => u.DisplayName).HasMaxLength(User.DisplayNameMaxLength).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(256).IsRequired();
        builder.Property(u => u.CreatedUtc).HasConversion(UtcConverter.Instance);
        builder.Property(u => u.LastSignInUtc).HasConversion(UtcConverter.Nullable);
        builder.Property(u => u.LockoutEndUtc).HasConversion(UtcConverter.Nullable);
    }
}

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSettings");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(100);
        builder.Property(s => s.Value).HasMaxLength(2000).IsRequired();
    }
}
