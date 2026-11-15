using System.Xml;
using System.Xml.Linq;

namespace Deckle.Projects;

// *.csproj and Directory.Build.props: the `<Version>` property of a
// PropertyGroup, else its `<VersionPrefix>`. Only a property counts: a
// `<Version>` element nested in an item (`<PackageReference><Version>`) is a
// dependency's. A value built from `$(...)` is a variable and gives nothing.
internal static class MsBuildManifest
{
    private static readonly XmlReaderSettings Settings = new()
    {
        // A project file never needs a DTD, and reading one is how a crafted
        // file would reach out of the folder.
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    // Throws XmlException when the text is not XML; the caller turns that into
    // "no source" and one Verbose event.
    public static string? Version(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text.TrimStart('﻿')), Settings);
        XDocument document = XDocument.Load(reader);
        XElement? project = document.Root;
        if (project is null || project.Name.LocalName != "Project") return null;

        List<XElement> properties = [.. project.Elements()
            .Where(e => e.Name.LocalName == "PropertyGroup")
            .SelectMany(g => g.Elements())];

        return First(properties, "Version") ?? First(properties, "VersionPrefix");
    }

    private static string? First(List<XElement> properties, string name) =>
        properties
            .Where(p => p.Name.LocalName == name)
            .Select(p => VersionText.Plain(p.Value))
            .FirstOrDefault(v => v is not null);
}
