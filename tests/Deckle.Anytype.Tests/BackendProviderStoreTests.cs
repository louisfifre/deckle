using System.IO.Compression;
using Deckle.Anytype;
using Deckle.Install;
using Xunit;

namespace Deckle.Anytype.Tests;

public sealed class BackendProviderStoreTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Failed_installation_leaves_previous_version_active()
    {
        string root = NewRoot();
        try
        {
            var store = NewStore(root);
            string oldDirectory = store.VersionDirectory("1.0.0");
            Directory.CreateDirectory(oldDirectory);
            await File.WriteAllTextAsync(Path.Combine(oldDirectory, "anytype.exe"), "old", Ct);
            store.Activate("1.0.0");

            string invalidZip = Path.Combine(root, "invalid.zip");
            using (ZipArchive archive = ZipFile.Open(invalidZip, ZipArchiveMode.Create))
                archive.CreateEntry("readme.txt");

            bool installed = await store.InstallFromZipAsync(invalidZip, "2.0.0", Ct);

            Assert.False(installed);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(oldDirectory, "anytype.exe")),
                store.ResolveActiveSpec()?.ExecutablePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Activated_version_is_complete_before_it_becomes_current()
    {
        string root = NewRoot();
        try
        {
            var store = NewStore(root);
            string zip = Path.Combine(root, "provider.zip");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                ZipArchiveEntry executable = archive.CreateEntry("wrapped/anytype.exe");
                await using Stream writer = executable.Open();
                await writer.WriteAsync("provider"u8.ToArray(), Ct);
            }

            Assert.True(await store.InstallFromZipAsync(zip, "2.0.0", Ct));

            BackendProcessSpec spec = Assert.IsType<BackendProcessSpec>(store.ResolveActiveSpec());
            Assert.Equal(
                Path.GetFullPath(Path.Combine(store.VersionDirectory("2.0.0"), "anytype.exe")),
                spec.ExecutablePath);
            Assert.True(File.Exists(spec.ExecutablePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Only_direct_version_executables_are_trusted()
    {
        string root = NewRoot();
        try
        {
            var store = NewStore(root);
            string direct = Path.Combine(store.VersionDirectory("1.0.0"), "anytype.exe");
            string nested = Path.Combine(store.VersionDirectory("2.0.0"), "debris", "anytype.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(direct)!);
            Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
            await File.WriteAllTextAsync(direct, "direct", Ct);
            await File.WriteAllTextAsync(nested, "nested", Ct);

            IReadOnlyList<string> trusted = store.TrustedExecutablePaths();

            Assert.Contains(Path.GetFullPath(direct), trusted);
            Assert.DoesNotContain(Path.GetFullPath(nested), trusted);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task Legacy_provider_is_adoptable_but_never_a_spawn_spec()
    {
        string root = NewRoot();
        try
        {
            var store = NewStore(root);
            Directory.CreateDirectory(store.LegacyDirectory);
            await File.WriteAllTextAsync(store.LegacyExecutablePath, "legacy", Ct);

            Assert.Null(store.ResolveActiveSpec());
            Assert.Contains(store.LegacyExecutablePath, store.TrustedExecutablePaths());

            Assert.True(await store.MigrateLegacyAsync("1.0.0", Ct));
            BackendProcessSpec spec = Assert.IsType<BackendProcessSpec>(store.ResolveActiveSpec());
            Assert.Equal(
                Path.Combine(store.VersionDirectory("1.0.0"), "anytype.exe"),
                spec.ExecutablePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task Activation_manifest_selects_only_its_direct_version_executable()
    {
        string root = NewRoot();
        try
        {
            var store = NewStore(root);
            string direct = Path.Combine(store.VersionDirectory("1.0.0"), "anytype.exe");
            string nested = Path.Combine(store.VersionDirectory("1.0.0"), "debris", "anytype.exe");
            string staging = Path.Combine(store.ProviderDirectory, "staging", "partial", "anytype.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(direct)!);
            Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
            Directory.CreateDirectory(Path.GetDirectoryName(staging)!);
            await File.WriteAllTextAsync(direct, "direct", Ct);
            await File.WriteAllTextAsync(nested, "nested", Ct);
            await File.WriteAllTextAsync(staging, "staging", Ct);

            await WriteActivationAsync(store, "1.0.0", "versions/1.0.0/debris/anytype.exe");
            Assert.Null(store.ResolveActiveSpec());

            await WriteActivationAsync(store, "1.0.0", "staging/partial/anytype.exe");
            Assert.Null(store.ResolveActiveSpec());

            await WriteActivationAsync(store, "2.0.0", "versions/1.0.0/anytype.exe");
            Assert.Null(store.ResolveActiveSpec());

            await WriteActivationAsync(store, "1.0.0", "versions/1.0.0/anytype.exe");
            Assert.Equal(Path.GetFullPath(direct), store.ResolveActiveSpec()?.ExecutablePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task Concurrent_legacy_migrations_publish_one_complete_active_version()
    {
        string root = NewRoot();
        try
        {
            string mutexName = $"Deckle.Anytype.Provider.Tests.{Guid.NewGuid():N}";
            var coordinator = new ArrivalGatePublicationCoordinator(
                new BackendProviderPublicationLease(mutexName));
            var first = NewStore(root, coordinator);
            var second = NewStore(root, coordinator);
            Directory.CreateDirectory(first.LegacyDirectory);
            await File.WriteAllTextAsync(first.LegacyExecutablePath, "legacy", Ct);

            Task<bool> firstMigration = first.MigrateLegacyAsync("1.0.0", Ct);
            Task<bool> secondMigration = second.MigrateLegacyAsync("1.0.0", Ct);
            await coordinator.BothArrived.Task.WaitAsync(Ct);
            coordinator.Release.TrySetResult();

            Assert.True(await firstMigration);
            Assert.True(await secondMigration);
            Assert.Equal(
                Path.Combine(first.VersionDirectory("1.0.0"), "anytype.exe"),
                first.ResolveActiveSpec()?.ExecutablePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Provider_root_is_outside_replaceable_application_payload()
    {
        string provider = Path.GetFullPath(BackendInstallation.InstallDirectory);
        string payload = Path.GetFullPath(InstallPaths.DefaultInstallDir)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        Assert.False(provider.StartsWith(payload, StringComparison.OrdinalIgnoreCase));
    }

    private static BackendProviderStore NewStore(
        string root,
        IBackendProviderPublicationCoordinator? coordinator = null) => new(
        Path.Combine(root, "provider"),
        Path.Combine(root, "payload", "anytype"),
        publicationCoordinator: coordinator);

    private static Task WriteActivationAsync(
        BackendProviderStore store,
        string version,
        string relativeExecutable) => File.WriteAllTextAsync(
            store.ActivationPath,
            $$"""{"Version":"{{version}}","RelativeExecutable":"{{relativeExecutable}}"}""",
            Ct);

    private static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"deckle-provider-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class ArrivalGatePublicationCoordinator(
        IBackendProviderPublicationCoordinator inner) : IBackendProviderPublicationCoordinator
    {
        private int _arrivals;
        public TaskCompletionSource BothArrived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Run(Action action, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _arrivals) == 2) BothArrived.TrySetResult();
            Release.Task.WaitAsync(ct).GetAwaiter().GetResult();
            inner.Run(action, ct);
        }
    }
}
