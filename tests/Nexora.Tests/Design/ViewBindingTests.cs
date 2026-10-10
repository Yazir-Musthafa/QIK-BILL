using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Nexora.Tests.Design;

/// <summary>
/// WPF reports a binding to a missing property or command only at runtime, and only in the debug output, so a typo
/// compiles and silently does nothing. Until the UI can be run on Windows, this test checks every binding a view makes
/// against its own view model (found through Views/ViewTemplates.xaml) and fails if the member does not exist.
///
/// Scope: bindings evaluated against the view's DataContext, including DataContext.X through RelativeSource to the
/// UserControl or Window. Bindings inside item templates and grid columns refer to row types and are not checked.
/// </summary>
public sealed partial class ViewBindingTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string DesktopRoot = Path.Combine(Root, "src", "Nexora.Desktop");

    [Fact]
    public void Every_view_binding_resolves_to_a_view_model_member()
    {
        var classes = ReadClasses();
        var views = ViewToViewModel();
        var problems = new List<string>();
        var checkedCount = 0;

        foreach (var file in Directory.EnumerateFiles(DesktopRoot, "*.xaml", SearchOption.AllDirectories).Where(f => !IsBuildOutput(f)))
        {
            var view = Path.GetFileNameWithoutExtension(file);
            if (!views.TryGetValue(view, out var viewModel)) continue;
            var members = Members(viewModel, classes, []);
            if (members.Count == 0)
            {
                problems.Add($"{view}: view model {viewModel} was not found in the Desktop sources.");
                continue;
            }

            Walk(XDocument.Load(file).Root!, inTemplate: false);

            void Walk(XElement element, bool inTemplate)
            {
                var name = element.Name.LocalName;
                var template = inTemplate || name is "DataTemplate" or "ItemsPanelTemplate" or "ControlTemplate" or "HierarchicalDataTemplate"
                    || name.EndsWith("Column", StringComparison.Ordinal);
                foreach (var attribute in element.Attributes())
                {
                    if (!attribute.Value.Contains("{Binding", StringComparison.Ordinal)) continue;
                    Check(attribute.Value, template);
                    // A bound DataContext changes the context of everything below it.
                    if (attribute.Name.LocalName == "DataContext") template = true;
                }

                if (name == "Binding" && !template && element.Attribute("RelativeSource") is null && element.Attribute("ElementName") is null
                    && element.Attribute("Path")?.Value is { Length: > 0 } path)
                {
                    checkedCount++;
                    if (!members.Contains(path.Split('.')[0])) problems.Add($"{view}: <Binding Path=\"{path}\"> is not a member of {viewModel}.");
                }

                foreach (var child in element.Elements()) Walk(child, template);
            }

            void Check(string value, bool inTemplate)
            {
                foreach (Match match in BindingPattern().Matches(value))
                {
                    var path = match.Groups["path"].Value;
                    var rest = match.Groups["rest"].Value;
                    if (rest.Contains("ElementName", StringComparison.Ordinal)) continue;
                    if (rest.Contains("RelativeSource", StringComparison.Ordinal))
                    {
                        var toRoot = rest.Contains("AncestorType=UserControl", StringComparison.Ordinal) || rest.Contains("AncestorType=Window", StringComparison.Ordinal);
                        if (!toRoot || !path.StartsWith("DataContext.", StringComparison.Ordinal)) continue;
                        path = path["DataContext.".Length..];
                    }
                    else if (inTemplate)
                    {
                        continue;
                    }

                    if (path.Length == 0) continue;
                    checkedCount++;
                    if (!members.Contains(path.Split('.')[0])) problems.Add($"{view}: \"{path}\" is not a member of {viewModel}.");
                }
            }
        }

        Assert.True(checkedCount > 300, $"Only {checkedCount} bindings were checked; the scan is probably broken.");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static Dictionary<string, string> ViewToViewModel()
    {
        var text = File.ReadAllText(Path.Combine(DesktopRoot, "Views", "ViewTemplates.xaml"));
        var map = TemplatePattern().Matches(text).ToDictionary(m => m.Groups["view"].Value, m => m.Groups["vm"].Value);
        map["MainWindow"] = "MainViewModel";
        return map;
    }

    private static Dictionary<string, string> ReadClasses()
    {
        var classes = new Dictionary<string, string>();
        foreach (var file in Directory.EnumerateFiles(DesktopRoot, "*.cs", SearchOption.AllDirectories).Where(f => !IsBuildOutput(f)))
        {
            var text = File.ReadAllText(file);
            var declarations = TypeDeclarationPattern().Matches(text).ToList();
            for (var i = 0; i < declarations.Count; i++)
            {
                if (declarations[i].Groups["kind"].Value != "class") continue;
                var end = i + 1 < declarations.Count ? declarations[i + 1].Index : text.Length;
                classes[declarations[i].Groups["name"].Value] = text[declarations[i].Index..end];
            }
        }

        return classes;
    }

    private static HashSet<string> Members(string type, Dictionary<string, string> classes, HashSet<string> seen)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);
        if (!classes.TryGetValue(type, out var body) || !seen.Add(type)) return members;
        foreach (Match m in ObservablePattern().Matches(body)) members.Add(char.ToUpperInvariant(m.Groups["field"].Value[0]) + m.Groups["field"].Value[1..]);
        foreach (Match m in CommandPattern().Matches(body))
        {
            var method = m.Groups["method"].Value;
            members.Add((method.EndsWith("Async", StringComparison.Ordinal) ? method[..^5] : method) + "Command");
        }

        foreach (Match m in PublicMemberPattern().Matches(body)) members.Add(m.Groups["name"].Value);
        if (BasePattern().Match(body) is { Success: true } baseMatch) members.UnionWith(Members(baseMatch.Groups["base"].Value, classes, seen));
        return members;
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Nexora.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex(@"\{Binding(?:\s+(?:Path=)?(?<path>[A-Za-z_][\w.]*)(?![\w.]|\s*=))?(?<rest>[^}]*)\}")]
    private static partial Regex BindingPattern();

    [GeneratedRegex(@"DataType=""\{x:Type \w+:(?<vm>\w+)\}""><\w+:(?<view>\w+) />")]
    private static partial Regex TemplatePattern();

    [GeneratedRegex(@"\b(?<kind>class|record|interface|enum)\s+(?<name>\w+)")]
    private static partial Regex TypeDeclarationPattern();

    [GeneratedRegex(@"\[ObservableProperty\][^\n]*?\s*(?:\[[^\]]+\]\s*)*private\s+[\w<>?,.\[\] ]+?\s+_(?<field>\w+)")]
    private static partial Regex ObservablePattern();

    [GeneratedRegex(@"\[RelayCommand[^\]]*\]\s*(?:public|private|internal)\s+(?:async\s+)?[\w<>?]+\s+(?<method>\w+)\(")]
    private static partial Regex CommandPattern();

    [GeneratedRegex(@"public\s+(?:required\s+|static\s+|override\s+)?[\w<>?,\[\]. ]+?\s+(?<name>\w+)\s*(?:\{|=>)")]
    private static partial Regex PublicMemberPattern();

    [GeneratedRegex(@"class\s+\w+\b[^{]*?:\s*(?<base>\w+)")]
    private static partial Regex BasePattern();
}
