using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Nexora.Infrastructure.Persistence.Configurations;

/// <summary>Values are stored as UTC and read back with <see cref="DateTimeKind.Utc"/>.</summary>
internal static class UtcConverter
{
    public static readonly ValueConverter<DateTime, DateTime> Instance = new(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    public static readonly ValueConverter<DateTime?, DateTime?> Nullable = new(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
