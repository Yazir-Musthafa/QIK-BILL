using System.Security.Cryptography;
using System.Text.Json;
using Nexora.Application.Security;

namespace Nexora.Licensing;

public static class ConfigureServer
{
    public static void Run(string directory)
    {
        directory = Path.GetFullPath(directory);
        var path = Path.Combine(directory, "settings.json");
        if (File.Exists(path)) throw new InvalidOperationException("Settings already exist. Keep the signing key; do not regenerate it for an existing installation.");
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Console.Write("Administrator username [admin]: ");
        var username = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(username)) username = "admin";
        Console.Write("Administrator password (hidden): ");
        var password = ReadPassword();
        Console.Write("Confirm password (hidden): ");
        if (password != ReadPassword()) throw new InvalidOperationException("Passwords do not match.");
        if (PasswordPolicy.Check(password, username) is { } problem) throw new InvalidOperationException(problem);
        using var key = RSA.Create(3072);
        File.WriteAllText(path, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Licensing"] = new { DataDirectory = directory, AdminUsername = username,
                AdminPasswordHash = new Pbkdf2PasswordHasher().Hash(password), SigningKeyPem = key.ExportPkcs8PrivateKeyPem() }
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.WriteAllText(Path.Combine(directory, "public-key.txt"), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        Console.WriteLine($"Settings saved to {path}. Keep this directory private and backed up. The public-key.txt file is safe to embed in the installer.");
    }
    private static string ReadPassword()
    {
        var result = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return result.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (result.Length > 0) result.Length--; }
            else if (!char.IsControl(key.KeyChar) && result.Length < 128) result.Append(key.KeyChar);
        }
    }
}
