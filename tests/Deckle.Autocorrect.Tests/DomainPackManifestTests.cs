using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The dilution indicator: what a pack brings and what fabrication refused to
// keep base corrections working (CONTEXT.md § Pack sanitization). The numbers
// are measured at build and shipped in a manifest beside the forms, so the
// product to guard is that the two agree — a manifest quoting a count the
// artifact does not carry would state a falsehood in the settings page, and
// nothing else would notice.
[Trait("Category", "unit")]
public class DomainPackManifestTests
{
    // The test binaries copy the module's Data/ next to themselves, so the
    // shipped artifacts are readable exactly as the app reads them.
    private static string DataDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Data");

    [Fact]
    public void EveryShippedPackShipsItsManifest()
    {
        foreach (DomainPack pack in DomainPack.Shipped)
        {
            Assert.True(
                File.Exists(Path.Combine(DataDirectory, pack.ManifestFileName)),
                $"Pack '{pack.Id}' ships forms without a dilution manifest.");
        }
    }

    // The load-bearing one. The manifest is written by the fabrication gesture
    // and the artifact by the same run; if a pack is ever rebuilt without its
    // manifest, or edited by hand, this is what says so.
    [Fact]
    public void ManifestShippedFormsMatchesTheArtifact()
    {
        foreach (DomainPack pack in DomainPack.Shipped)
        {
            DomainPackManifest? manifest = DomainPackManifest.TryLoad(DataDirectory, pack);
            Assert.NotNull(manifest);

            FrequencyLexicon? forms = pack.TryLoad(DataDirectory);
            Assert.NotNull(forms);

            Assert.Equal(forms.Count, manifest.ShippedForms);
        }
    }

    [Fact]
    public void ManifestIdentifiesThePackItShipsWith()
    {
        foreach (DomainPack pack in DomainPack.Shipped)
        {
            DomainPackManifest? manifest = DomainPackManifest.TryLoad(DataDirectory, pack);

            Assert.NotNull(manifest);
            Assert.Equal(pack.Id, manifest.Id);
        }
    }

    // A pack still holding unjudged gray-zone candidates is unfinished: those
    // forms are withheld from the artifact, so the pack ships less than it
    // mined. Shipping one would be a fabrication slip, not a user-facing state.
    [Fact]
    public void ShippedPacksHaveNoFormLeftPendingJudgment()
    {
        foreach (DomainPack pack in DomainPack.Shipped)
        {
            DomainPackManifest? manifest = DomainPackManifest.TryLoad(DataDirectory, pack);

            Assert.NotNull(manifest);
            Assert.Equal(0, manifest.PendingJudgment);
        }
    }

    // Absence is a miss, never a throw: a build without the manifest shows no
    // figures and still corrects.
    [Fact]
    public void TryLoad_ReturnsNullWhenNoManifestShips()
    {
        var absent = new DomainPack("fr-nothing", "computing", "fr");

        Assert.Null(DomainPackManifest.TryLoad(DataDirectory, absent));
    }
}
