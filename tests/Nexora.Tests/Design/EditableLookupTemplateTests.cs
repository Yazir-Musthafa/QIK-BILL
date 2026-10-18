using System.Xml.Linq;

namespace Nexora.Tests.Design;

public sealed class EditableLookupTemplateTests
{
    [Fact]
    public void The_shared_combo_template_provides_the_Wpf_editing_part_and_an_editable_state()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Nexora.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var document = XDocument.Load(Path.Combine(directory.FullName, "src", "Nexora.Desktop", "Themes", "Controls.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var template = Assert.Single(document.Descendants(),
            e => e.Name.LocalName == "ControlTemplate" && (string?)e.Attribute("TargetType") == "ComboBox");
        Assert.Contains(template.Descendants(), e => e.Name.LocalName == "TextBox" && (string?)e.Attribute(x + "Name") == "PART_EditableTextBox");
        var trigger = Assert.Single(template.Descendants(), e => e.Name.LocalName == "Trigger" && (string?)e.Attribute("Property") == "IsEditable");
        Assert.Equal("True", (string?)trigger.Attribute("Value"));
        Assert.Contains(trigger.Elements(), e => (string?)e.Attribute("TargetName") == "PART_EditableTextBox"
            && (string?)e.Attribute("Property") == "Visibility" && (string?)e.Attribute("Value") == "Visible");
        Assert.Contains(trigger.Elements(), e => (string?)e.Attribute("TargetName") == "DropDownToggle"
            && (string?)e.Attribute("Property") == "HorizontalAlignment" && (string?)e.Attribute("Value") == "Right");
    }
}
