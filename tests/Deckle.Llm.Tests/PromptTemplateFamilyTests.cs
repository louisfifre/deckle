using Xunit;

namespace Deckle.Llm.Tests;

// What each local model family is sent in Ollama's raw mode, and which tokens
// stop it. A wrong template does not fail loudly: the model answers badly or
// never stops, and the rewrite the user reads is garbage.
[Trait("Category", "unit")]
public sealed class PromptTemplateFamilyTests
{
    private const string System = "Corrige le texte.";
    private const string User = "bonjour tout le mond";

    [Theory]
    [InlineData("mistral:7b-instruct")]
    [InlineData("ministral-8b")]
    [InlineData("mixtral:8x7b")]
    public void MistralModelsAreAskedInAnInstBlockAndStoppedOnItsMarkers(string model)
    {
        var (prompt, stops, family) = PromptTemplates.Build(model, System, User);

        Assert.Equal("mistral", family);
        Assert.Equal($"[INST] {System}\n\n{User} [/INST]", prompt);
        Assert.Equal(new[] { "[INST]", "[/INST]", "</s>" }, stops);
    }

    [Theory]
    [InlineData("llama3.1:8b")]
    [InlineData("meta-llama-3-8b-instruct")]
    [InlineData("llama-3.2-3b")]
    public void Llama3ModelsGetTheirHeaderTurnsAndStopOnEndOfTurn(string model)
    {
        var (prompt, stops, family) = PromptTemplates.Build(model, System, User);

        Assert.Equal("llama3", family);
        Assert.Equal(
            $"<|start_header_id|>system<|end_header_id|>\n\n{System}<|eot_id|>" +
            $"<|start_header_id|>user<|end_header_id|>\n\n{User}<|eot_id|>" +
            "<|start_header_id|>assistant<|end_header_id|>\n\n",
            prompt);
        Assert.Equal(new[] { "<|eot_id|>", "<|end_of_text|>" }, stops);
    }

    [Fact]
    public void QwenModelsGetChatMlTurnsAndStopOnEndOfMessage()
    {
        var (prompt, stops, family) = PromptTemplates.Build("qwen2.5:7b", System, User);

        Assert.Equal("qwen", family);
        Assert.Equal(
            $"<|im_start|>system\n{System}<|im_end|>\n<|im_start|>user\n{User}<|im_end|>\n<|im_start|>assistant\n",
            prompt);
        Assert.Equal(new[] { "<|im_end|>", "<|endoftext|>" }, stops);
    }

    [Fact]
    public void GemmaModelsHaveNoSystemTurnSoTheSystemPromptOpensTheUserTurn()
    {
        var (prompt, stops, family) = PromptTemplates.Build("gemma2:9b", System, User);

        Assert.Equal("gemma", family);
        Assert.Equal(
            $"<start_of_turn>user\n{System}\n\n{User}<end_of_turn>\n<start_of_turn>model\n",
            prompt);
        Assert.DoesNotContain("system", prompt);
        Assert.Equal(new[] { "<end_of_turn>", "<eos>" }, stops);
    }

    [Theory]
    [InlineData("phi3:mini")]
    [InlineData("phi-4")]
    public void PhiModelsGetTheirTaggedTurnsAndStopOnEnd(string model)
    {
        var (prompt, stops, family) = PromptTemplates.Build(model, System, User);

        Assert.Equal("phi", family);
        Assert.Equal(
            $"<|system|>\n{System}<|end|>\n<|user|>\n{User}<|end|>\n<|assistant|>\n",
            prompt);
        Assert.Equal(new[] { "<|end|>", "<|endoftext|>" }, stops);
    }

    [Theory]
    [InlineData("yi:34b")]
    [InlineData("deepseek-r1")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownModelFallsBackToChatMlRatherThanFailing(string? model)
    {
        var (prompt, stops, family) = PromptTemplates.Build(model!, System, User);

        Assert.Equal("chatml", family);
        Assert.Equal(
            $"<|im_start|>system\n{System}<|im_end|>\n<|im_start|>user\n{User}<|im_end|>\n<|im_start|>assistant\n",
            prompt);
        Assert.Equal(new[] { "<|im_end|>", "<|endoftext|>" }, stops);
    }

    [Fact]
    public void TheModelNameIsMatchedWithoutRegardToCase()
    {
        var (_, _, family) = PromptTemplates.Build("Mistral-7B-Instruct", System, User);

        Assert.Equal("mistral", family);
    }

    [Theory]
    [InlineData("mistral:7b", "[INST] " + User + " [/INST]")]
    [InlineData("gemma2:9b", "<start_of_turn>user\n" + User + "<end_of_turn>\n<start_of_turn>model\n")]
    public void ABlankSystemPromptLeavesNoEmptyGapBeforeTheUserText(string model, string expected)
    {
        var (prompt, _, _) = PromptTemplates.Build(model, "  ", User);

        Assert.Equal(expected, prompt);
    }

    [Theory]
    [InlineData("llama3:8b", "<|start_header_id|>system")]
    [InlineData("qwen2.5", "<|im_start|>system")]
    [InlineData("phi3", "<|system|>")]
    public void ABlankSystemPromptSendsNoSystemTurnAtAll(string model, string systemMarker)
    {
        var (prompt, _, _) = PromptTemplates.Build(model, "", User);

        Assert.DoesNotContain(systemMarker, prompt);
        Assert.Contains(User, prompt);
    }

    [Theory]
    [InlineData("mistral:7b")]
    [InlineData("llama3:8b")]
    [InlineData("qwen2.5")]
    [InlineData("gemma2")]
    [InlineData("phi3")]
    [InlineData("yi")]
    public void TheStopTokenAMistakenlyEchoedIsRemovedFromTheAnswerOfItsFamily(string model)
    {
        var (_, stops, family) = PromptTemplates.Build(model, System, User);

        foreach (var stop in stops)
            Assert.Equal("bonjour tout le monde", PromptTemplates.StripStops($"bonjour tout le monde{stop}", family));
    }
}
