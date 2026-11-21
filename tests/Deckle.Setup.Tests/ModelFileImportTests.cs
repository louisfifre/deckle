using Xunit;

namespace Deckle.Setup.Tests;

[Trait("Category", "regression")]
public sealed class ModelFileImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deckle-model-import-{Guid.NewGuid():N}");

    private string Write(string name, string content)
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string[] Partials() =>
        Directory.GetFiles(_root, "*.partial", SearchOption.AllDirectories);

    [Fact]
    public void A_wrong_hash_keeps_the_installed_model_and_leaves_no_temporary_file()
    {
        string source = Write("picked.bin", "new bytes");
        string dest = Write("model.bin", "installed model");

        bool ok = ModelFileImport.Install(source, dest, new string('0', 64));

        Assert.False(ok);
        Assert.Equal("installed model", File.ReadAllText(dest));
        Assert.Empty(Partials());
    }

    [Fact]
    public void A_wrong_hash_with_nothing_installed_leaves_no_file_at_the_destination()
    {
        string source = Write("picked.bin", "new bytes");
        string dest = Path.Combine(_root, "model.bin");

        Assert.False(ModelFileImport.Install(source, dest, new string('0', 64)));

        Assert.False(File.Exists(dest));
        Assert.Empty(Partials());
    }

    [Fact]
    public void A_copy_that_cannot_complete_keeps_the_installed_model_and_leaves_no_temporary_file()
    {
        string dest = Write("model.bin", "installed model");
        string missing = Path.Combine(_root, "gone.bin");

        Assert.ThrowsAny<IOException>(() => ModelFileImport.Install(missing, dest, null));

        Assert.Equal("installed model", File.ReadAllText(dest));
        Assert.Empty(Partials());
    }

    [Fact]
    public void A_matching_hash_replaces_the_installed_model_and_leaves_no_temporary_file()
    {
        string source = Write("picked.bin", "new bytes");
        string dest = Write("model.bin", "installed model");
        string hash = ModelFileImport.ComputeSha256(source).ToUpperInvariant();

        Assert.True(ModelFileImport.Install(source, dest, hash));

        Assert.Equal("new bytes", File.ReadAllText(dest));
        Assert.Empty(Partials());
    }

    [Fact]
    public void Without_an_expected_hash_the_file_is_installed_and_the_folder_is_created()
    {
        string source = Write("picked.bin", "new bytes");
        string dest = Path.Combine(_root, "models", "model.bin");

        Assert.True(ModelFileImport.Install(source, dest, ""));

        Assert.Equal("new bytes", File.ReadAllText(dest));
        Assert.Empty(Partials());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
