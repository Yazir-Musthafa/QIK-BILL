namespace Nexora.Domain.Settings;

/// <summary>Installation-wide key/value preference such as the selected theme.</summary>
public sealed class AppSetting
{
    private AppSetting()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    public AppSetting(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required.", nameof(key));
        Key = key;
        Value = value ?? string.Empty;
    }

    public string Key { get; private set; }

    public string Value { get; private set; }

    public void Update(string value) => Value = value ?? string.Empty;
}
