using System.IO;
using Microsoft.Win32;
using Nexora.Reporting.Export;

namespace Nexora.Desktop.Services;

internal static class DataFiles
{
    public static async Task SaveAsync(ExportFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        var dialog = new SaveFileDialog { FileName = file.FileName, DefaultExt = extension, AddExtension = true,
            Filter = $"{extension.TrimStart('.').ToUpperInvariant()} files (*{extension})|*{extension}" };
        if (dialog.ShowDialog() == true) await File.WriteAllBytesAsync(dialog.FileName, file.Content);
    }
    public static string? Folder(string current)
    {
        var dialog = new OpenFolderDialog { Title = "Choose backup folder", InitialDirectory = Directory.Exists(current) ? current : string.Empty };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
