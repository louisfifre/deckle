using System;
using System.IO;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Mlm;
using Deckle.Core;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Loads the model once (a ~440 MB ONNX load) for the whole class.
public sealed class CamembertModelFixture : IDisposable
{
    public ISentenceReranker? Reranker { get; }

    public CamembertModelFixture()
    {
        string dir = Path.Combine(AppPaths.ModelsDirectory, "camembert-base");
        // margin 0 → return the argmax; freqPrior 0 → pure model logits, so the
        // assertion measures the model's own discrimination, not the prior.
        if (File.Exists(Path.Combine(dir, "model.onnx")))
            Reranker = CamembertReranker.TryLoad(dir, margin: 0.0, freqPrior: 0.0);
    }

    public void Dispose() => (Reranker as IDisposable)?.Dispose();
}

// Loads the REAL CamemBERT ONNX model and checks it discriminates the function-word
// ambiguities live — the verification that the downloaded model is sound before the
// engine trusts it. Machine-specific: each test is a silent no-op when the model is
// absent (CI, a fresh clone), and runs only where setup-assets has staged it.
[Trait("Category", "integration")]
public class CamembertRerankerIntegrationTests : IClassFixture<CamembertModelFixture>
{
    private readonly CamembertModelFixture _model;

    public CamembertRerankerIntegrationTests(CamembertModelFixture model) => _model = model;

    private static AccentVariant[] LaLà() =>
        new[] { new AccentVariant("la", 0.0), new AccentVariant("là", 0.0) };

    [Fact]
    public void ModelLoadsWhenStaged()
    {
        string dir = Path.Combine(AppPaths.ModelsDirectory, "camembert-base");
        if (!File.Exists(Path.Combine(dir, "model.onnx")))
            return; // absent (CI/fresh clone): nothing to assert
        // Present: it MUST load — a corrupt or version-mismatched export fails here.
        Assert.NotNull(_model.Reranker);
    }

    [Fact]
    public void PicksTheAccentedFormWhereContextDemandsIt()
    {
        if (_model.Reranker is null) return;

        // « c'est là que ça se passe » — the locative "là", not the article "la".
        string? chosen = _model.Reranker.Rerank(
            new[] { "c'est", "là", "que", "ça", "se", "passe" }, slotIndex: 1, LaLà()).Chosen;

        Assert.Equal("là", chosen);
    }

    [Fact]
    public void KeepsTheBareFormWhereContextDemandsIt()
    {
        if (_model.Reranker is null) return;

        // « je vais à la mer » — the article "la", not the locative "là".
        string? chosen = _model.Reranker.Rerank(
            new[] { "je", "vais", "à", "la", "mer" }, slotIndex: 3, LaLà()).Chosen;

        Assert.Equal("la", chosen);
    }
}
