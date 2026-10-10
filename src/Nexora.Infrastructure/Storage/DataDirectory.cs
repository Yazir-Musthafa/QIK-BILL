namespace Nexora.Infrastructure.Storage;

/// <summary>Where the data folder ended up, and whether data was brought across from the old name.</summary>
public sealed record DataDirectoryResult(string Path, string DatabasePath, bool MigratedFromLegacy);

/// <summary>
/// Locates the per-user data folder and upgrades installations made before the product was renamed.
///
/// Current layout:  &lt;LocalAppData&gt;\QIKSuite\qiksuite.db (+ logs\)
/// Legacy layout:   &lt;LocalAppData&gt;\Nexora\BusinessSuite\nexora.db (+ logs\)
///
/// If the legacy folder exists and the new one does not, everything is copied into a temporary folder
/// beside the target and then renamed into place, so a failure part-way never leaves a half-copied
/// QIKSuite folder that would be mistaken for a fresh install. The legacy folder is left untouched as a backup.
/// Uses only portable file APIs, so it is tested on any OS.
/// </summary>
public static class DataDirectory
{
    public const string FolderName = "QIKSuite";
    public const string DatabaseFileName = "qiksuite.db";
    public const string LegacyDatabaseFileName = "nexora.db";

    private const string StagingSuffix = ".migrating";

    public static string LegacyPath(string localAppData) => System.IO.Path.Combine(localAppData, "Nexora", "BusinessSuite");

    public static DataDirectoryResult Prepare(string localAppData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppData);
        var target = System.IO.Path.Combine(localAppData, FolderName);
        var legacy = LegacyPath(localAppData);
        var migrated = false;

        if (!Directory.Exists(target) && Directory.Exists(legacy))
        {
            var staging = target + StagingSuffix;
            // A leftover staging folder means an earlier attempt was interrupted; start again from the legacy data.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);

            CopyLegacy(legacy, staging);
            Directory.Move(staging, target);
            migrated = true;
        }

        Directory.CreateDirectory(target);
        return new DataDirectoryResult(target, System.IO.Path.Combine(target, DatabaseFileName), migrated);
    }

    private static void CopyLegacy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(source, file);
            var name = System.IO.Path.GetFileName(relative);

            // SQLite's shared-memory index is rebuilt on open; copying a stale one is pointless.
            if (name.Equals(LegacyDatabaseFileName + "-shm", StringComparison.OrdinalIgnoreCase)) continue;

            // The database and its write-ahead log must keep matching names, so both are renamed together.
            if (name.StartsWith(LegacyDatabaseFileName, StringComparison.OrdinalIgnoreCase) && !relative.Contains(System.IO.Path.DirectorySeparatorChar))
                relative = DatabaseFileName + name[LegacyDatabaseFileName.Length..];

            var target = System.IO.Path.Combine(destination, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }
}
