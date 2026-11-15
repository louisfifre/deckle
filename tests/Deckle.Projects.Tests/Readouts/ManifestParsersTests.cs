using System.Text.Json;
using System.Xml;
using Xunit;

namespace Deckle.Projects.Tests;

// Each format gets its plain case and the traps its files really hold: a
// version under another table, a version the workspace owns, a version made of
// a variable, comments, a byte-order mark and Windows line endings.
[Trait("Category", "unit")]
public sealed class ManifestParsersTests
{
    // ── Cargo.toml ─────────────────────────────────────────────────────────

    [Fact]
    public void CargoReadsThePackageVersion()
    {
        string text = """
            [package]
            name = "launcher"
            version = "0.1.0"
            edition = "2021"
            """;
        Assert.Equal("0.1.0", CargoManifest.Version(text));
    }

    [Fact]
    public void CargoPrefersThePackageToTheWorkspaceAndFallsBackToIt()
    {
        string both = "[workspace.package]\nversion = \"2.0.0\"\n[package]\nversion = \"1.0.0\"\n";
        string workspaceOnly = "[workspace]\nmembers = [\"a\"]\n[workspace.package]\nversion = \"2.0.0\"\n";
        Assert.Equal("1.0.0", CargoManifest.Version(both));
        Assert.Equal("2.0.0", CargoManifest.Version(workspaceOnly));
    }

    [Fact]
    public void CargoGivesNothingForAMemberThatInheritsTheWorkspaceVersion()
    {
        Assert.Null(CargoManifest.Version("[package]\nname = \"a\"\nversion.workspace = true\n"));
        Assert.Null(CargoManifest.Version("[package]\nname = \"a\"\nversion = { workspace = true }\n"));
    }

    [Fact]
    public void CargoIgnoresAVersionKeyUnderAnotherTable()
    {
        string text = """
            [package]
            name = "a"

            [dependencies]
            serde = "1"

            [dependencies.tokio]
            version = "1.40.0"

            [[bin]]
            version = "9.9.9"
            """;
        Assert.Null(CargoManifest.Version(text));
    }

    [Fact]
    public void CargoSkipsCommentsAndReadsABomAndCrLf()
    {
        string text = "﻿# a comment\r\n[package] # the package\r\nname = \"a\" # not this\r\nversion = \"3.4.5\" # this one\r\n";
        // The BOM sits before the first line, which is a comment here.
        Assert.Equal("3.4.5", CargoManifest.Version(text));
    }

    [Fact]
    public void CargoReadsASingleQuotedVersion()
    {
        Assert.Equal("0.2.0", CargoManifest.Version("[package]\nversion = '0.2.0'\n"));
    }

    // ── pyproject.toml ─────────────────────────────────────────────────────

    [Fact]
    public void PyProjectReadsTheProjectVersionThenPoetry()
    {
        Assert.Equal("1.2.3", PyProjectManifest.Version("[project]\nname = \"x\"\nversion = \"1.2.3\"\n"));
        Assert.Equal("0.5.0", PyProjectManifest.Version("[tool.poetry]\nname = \"x\"\nversion = \"0.5.0\"\n"));
        Assert.Equal("1.0.0", PyProjectManifest.Version("[tool.poetry]\nversion = \"0.5.0\"\n[project]\nversion = \"1.0.0\"\n"));
    }

    [Fact]
    public void PyProjectGivesNothingForADynamicVersionOrAnotherTable()
    {
        Assert.Null(PyProjectManifest.Version("[project]\nname = \"x\"\ndynamic = [\"version\"]\n"));
        Assert.Null(PyProjectManifest.Version("[tool.black]\nversion = \"23.1\"\n[project]\nname = \"x\"\n"));
    }

    // ── package.json ───────────────────────────────────────────────────────

    [Fact]
    public void PackageJsonReadsTheRootVersionAndNotANestedOne()
    {
        string json = """
            { "name": "a", "dependencies": { "left-pad": { "version": "9.9.9" } }, "version": "2.1.0" }
            """;
        Assert.Equal("2.1.0", PackageJsonManifest.Version(json));
        Assert.Null(PackageJsonManifest.Version("""{ "name": "a", "dependencies": { "x": { "version": "1.0.0" } } }"""));
    }

    [Fact]
    public void PackageJsonToleratesCommentsTrailingCommasAndABom()
    {
        string json = "﻿{\n  // the version\n  \"version\": \"4.0.0\",\n}\n";
        Assert.Equal("4.0.0", PackageJsonManifest.Version(json));
    }

    [Fact]
    public void PackageJsonGivesNothingForAVersionThatIsNotAString()
    {
        Assert.Null(PackageJsonManifest.Version("""{ "version": 3 }"""));
        Assert.Null(PackageJsonManifest.Version("[1, 2]"));
    }

    [Fact]
    public void PackageJsonThatIsNotJsonThrowsForTheCallerToSwallow()
    {
        Assert.ThrowsAny<JsonException>(() => PackageJsonManifest.Version("not json"));
    }

    // ── MSBuild ────────────────────────────────────────────────────────────

    [Fact]
    public void MsBuildReadsVersionThenVersionPrefix()
    {
        string both = "<Project><PropertyGroup><VersionPrefix>1.0.0</VersionPrefix><Version>1.2.3-beta</Version></PropertyGroup></Project>";
        string prefix = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><VersionPrefix>1.4.0</VersionPrefix></PropertyGroup></Project>";
        Assert.Equal("1.2.3-beta", MsBuildManifest.Version(both));
        Assert.Equal("1.4.0", MsBuildManifest.Version(prefix));
    }

    [Fact]
    public void MsBuildIgnoresADependencysVersionAndAVariable()
    {
        string item = "<Project><ItemGroup><PackageReference Include=\"X\"><Version>9.9.9</Version></PackageReference></ItemGroup></Project>";
        string variable = "<Project><PropertyGroup><Version>$(MajorVersion).0</Version></PropertyGroup></Project>";
        Assert.Null(MsBuildManifest.Version(item));
        Assert.Null(MsBuildManifest.Version(variable));
    }

    [Fact]
    public void MsBuildTakesTheFirstPlainValueAndReadsABomAndNamespace()
    {
        string text = "﻿<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\r\n"
            + "<PropertyGroup Condition=\"'$(X)'=='1'\"><Version>$(Y)</Version></PropertyGroup>\r\n"
            + "<PropertyGroup><Version>0.7.0</Version></PropertyGroup></Project>";
        Assert.Equal("0.7.0", MsBuildManifest.Version(text));
    }

    [Fact]
    public void MsBuildThatIsNotXmlThrowsForTheCallerToSwallow()
    {
        Assert.ThrowsAny<XmlException>(() => MsBuildManifest.Version("<Project><"));
    }

    [Fact]
    public void MsBuildRefusesADtd()
    {
        string text = "<!DOCTYPE Project [<!ENTITY x \"y\">]><Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>";
        Assert.ThrowsAny<XmlException>(() => MsBuildManifest.Version(text));
    }

    // ── Gradle ─────────────────────────────────────────────────────────────

    [Fact]
    public void GradlePropertiesReadModVersion()
    {
        string text = "# mod\r\norg.gradle.jvmargs=-Xmx1G\r\nmod_id=voice\r\nmod_version = 0.1.0\r\n";
        Assert.Equal("0.1.0", GradleManifest.PropertiesVersion(text));
    }

    [Fact]
    public void GradlePropertiesIgnoreACommentedOrAnotherKey()
    {
        Assert.Null(GradleManifest.PropertiesVersion("#mod_version=9\nmc_version=1.21\nversion=3.0\n"));
        Assert.Null(GradleManifest.PropertiesVersion("mod_version=${base}\n"));
    }

    [Theory]
    [InlineData("plugins { id 'java' }\nversion = '1.2.3'\n", "1.2.3")]
    [InlineData("group 'x'\nversion '2.0.0'\n", "2.0.0")]
    [InlineData("version = \"3.1\" // the release\r\n", "3.1")]
    [InlineData("  version='0.0.1'\n", "0.0.1")]
    public void BuildGradleReadsTheProjectsOwnVersion(string text, string expected)
    {
        Assert.Equal(expected, GradleManifest.BuildScriptVersion(text));
    }

    [Theory]
    [InlineData("plugins { id 'x' version '1.0.0' }\n")]
    [InlineData("dependencies {\n  implementation 'a:b:1.0'\n}\n")]
    [InlineData("version = \"${base}-SNAPSHOT\"\n")]
    [InlineData("def version = 3\n")]
    public void BuildGradleIgnoresWhatIsNotTheProjectsVersion(string text)
    {
        Assert.Null(GradleManifest.BuildScriptVersion(text));
    }

    // ── Dispatch by file name ──────────────────────────────────────────────

    [Theory]
    [InlineData("Cargo.toml", true)]
    [InlineData("package.json", true)]
    [InlineData("App.csproj", true)]
    [InlineData("Directory.Build.props", true)]
    [InlineData("pyproject.toml", true)]
    [InlineData("gradle.properties", true)]
    [InlineData("build.gradle", true)]
    [InlineData("Directory.Packages.props", false)]
    [InlineData("package-lock.json", false)]
    [InlineData("build.gradle.kts", false)]
    [InlineData("README.md", false)]
    public void OnlyTheStandardManifestNamesAreRead(string fileName, bool expected)
    {
        Assert.Equal(expected, ManifestVersions.IsManifestName(fileName));
    }

    [Fact]
    public void TheNameDecidesWhichParserReadsTheText()
    {
        Assert.Equal("1.0.0", ManifestVersions.Parse("Cargo.toml", "[package]\nversion = \"1.0.0\"\n"));
        Assert.Equal("2.0.0", ManifestVersions.Parse("package.json", "{\"version\":\"2.0.0\"}"));
        Assert.Equal("3.0.0", ManifestVersions.Parse("Directory.Build.props", "<Project><PropertyGroup><Version>3.0.0</Version></PropertyGroup></Project>"));
        Assert.Null(ManifestVersions.Parse("README.md", "version = '1'"));
    }
}
