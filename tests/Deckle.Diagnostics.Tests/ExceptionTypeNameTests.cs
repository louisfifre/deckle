using System.Text.RegularExpressions;
using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "unit")]
public sealed class ExceptionTypeNameTests
{
    [Fact]
    public void The_name_carries_no_namespace()
    {
        Assert.Equal("InvalidOperationException", ExceptionTypeName.Of(new InvalidOperationException()));
        Assert.Equal("IOException", ExceptionTypeName.Of(new IOException()));
    }

    [Fact]
    public void No_exception_gives_an_empty_value_never_null()
    {
        Assert.Equal("", ExceptionTypeName.Of(null));
    }

    // The doctrine: one spelling of ex_type across providers. The namespace-
    // qualified spelling was the one that strayed, so a provider that asks the
    // type for its FullName brings the split back. Moving every remaining
    // `GetType().Name` onto the helper is a second pass, so this test holds
    // only the line that is already clean.
    [Fact]
    [Trait("Category", "contract")]
    public void No_provider_computes_ex_type_with_the_full_type_name()
    {
        string src = Path.Combine(RepoRoot(), "src");
        var offenders = new List<string>();
        var byHand = new Regex(@"GetType\(\)\s*\.\s*FullName", RegexOptions.Compiled);

        foreach (string file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
                if (byHand.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetRelativePath(src, file)}:{i + 1}");
        }

        Assert.True(offenders.Count == 0,
            "Use ExceptionTypeName.Of(ex) for ex_type instead of GetType().FullName:\n" + string.Join("\n", offenders));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
