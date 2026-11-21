using System.Net.Http;
using Deckle.Folders;
using Deckle.Llm;
using Deckle.Llm.Rewrite;

namespace Deckle.Folders.App;

// ─── Deckle Folders' photo descriptions, over Deckle.Llm ──────────────────────
//
// The adapter between Deckle Folders' seam (IPhotoDescriptionModel, a picture
// and a prompt in, a description or a typed failure out) and Deckle.Llm's
// engines. It lives in the host because the host's job is to aggregate the
// modules by role (deckle-modularity): Deckle.Folders stays free of any
// model, server or provider list, Deckle.Llm stays free of photos, and
// neither references the other. Only the host references both, and the
// provider list it reads (LlmSettingsService) as well.
//
// What it decides, per request:
// - which provider and model: the one stored choice of Deckle Folders
//   (PhotoDescriptionModelChoiceStore), read every time so a new choice
//   applies to the next photo; nothing stored is "no model chosen";
// - that the provider is on this machine: a hosted kind, or an address that
//   is not loopback, is refused here already, before the engine refuses it
//   again (LocalEndpoint);
// - how long one photo may take: five minutes, past which it counts as
//   failed. A vision model on a CPU takes tens of seconds; five minutes is a
//   server that hangs.
// - the human-facing kind of each failure.
internal sealed class LlmPhotoDescriptionModel : IPhotoDescriptionModel
{
    internal static readonly TimeSpan PhotoDeadline = TimeSpan.FromMinutes(5);

    // A short factual answer: little room for the model to wander.
    private const double Temperature = 0.2;

    private const string Label = "photo-description";

    private readonly Func<PhotoDescriptionModelChoice?> _choice;
    private readonly Func<IReadOnlyList<LlmProvider>> _providers;
    private readonly IGenerationEngineResolver _engines;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _deadline;

    internal LlmPhotoDescriptionModel(
        Func<PhotoDescriptionModelChoice?> choice,
        Func<IReadOnlyList<LlmProvider>> providers,
        IGenerationEngineResolver engines,
        TimeProvider? clock = null,
        TimeSpan? deadline = null)
    {
        _choice = choice;
        _providers = providers;
        _engines = engines;
        _clock = clock ?? TimeProvider.System;
        _deadline = deadline ?? PhotoDeadline;
    }

    /// <summary>The production adapter: the choice stored in Deckle Folders'
    /// data folder, the provider list the rewrite settings edit.</summary>
    public static LlmPhotoDescriptionModel Create(string moduleDirectory)
    {
        var choices = new PhotoDescriptionModelChoiceStore(moduleDirectory);
        Func<IReadOnlyList<LlmProvider>> providers = () => LlmSettingsService.Instance.Current.Providers;
        return new LlmPhotoDescriptionModel(choices.Read, providers, LlmProviderEngines.CreateDefault(providers));
    }

    /// <summary>What stops every description as the model is set up, found
    /// without asking a server: null when a request may leave. Deckle Folders'
    /// panel and folder command read it first, so they say what is missing
    /// before offering a button that can only fail.</summary>
    public PhotoDescriptionFailure? Readiness() => Resolve(out _, out _, out _);

    public async Task<PhotoDescriptionResult> DescribeAsync(byte[] jpeg, string prompt, CancellationToken cancellationToken)
    {
        if (Resolve(out PhotoDescriptionModelChoice? choice, out _, out IGenerationEngine? engine) is { } notReady)
            return Failed(notReady);

        var request = new GenerationRequest(
            Model: choice!.Model,
            SystemPrompt: "",
            UserText: prompt,
            Label: Label,
            Temperature: Temperature,
            Image: new GenerationImage(jpeg, "image/jpeg"));

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_deadline);
        try
        {
            // The engines block on their HTTP call: off the caller's thread.
            GenerationResult result = await Task.Run(() => engine!.Generate(request, deadline.Token), deadline.Token);
            if (string.IsNullOrWhiteSpace(result.Text)) return Failed(PhotoDescriptionFailure.Other);
            return PhotoDescriptionResult.Of(new PhotoDescription(result.Text, choice.Model, _clock.GetLocalNow()));
        }
        catch (Exception ex)
        {
            return Failed(KindOf(ex, cancellationToken));
        }
    }

    // The stored choice, its provider and its engine, or why there is none.
    private PhotoDescriptionFailure? Resolve(
        out PhotoDescriptionModelChoice? choice, out LlmProvider? provider, out IGenerationEngine? engine)
    {
        provider = null;
        engine = null;
        choice = _choice();
        if (choice is not { IsComplete: true }) return PhotoDescriptionFailure.NoModelChosen;

        string providerId = choice.ProviderId;
        provider = _providers().FirstOrDefault(p => p.Id == providerId);
        if (provider is not { Enabled: true }) return PhotoDescriptionFailure.NoModelChosen;
        if (provider.Kind == LlmProviderKind.Claude || !LocalEndpoint.IsLoopback(provider.Endpoint))
            return PhotoDescriptionFailure.EndpointNotLocal;

        engine = _engines.Resolve(provider.Id);
        return engine is null ? PhotoDescriptionFailure.NoModelChosen : null;
    }

    internal static PhotoDescriptionFailure KindOf(Exception ex, CancellationToken caller) => ex switch
    {
        OperationCanceledException when caller.IsCancellationRequested => PhotoDescriptionFailure.Stopped,
        // The deadline, not the person.
        OperationCanceledException => PhotoDescriptionFailure.Other,
        LocalEndpointRequiredException => PhotoDescriptionFailure.EndpointNotLocal,
        ImageInputNotSupportedException => PhotoDescriptionFailure.ImagesNotSupported,
        // No status: the request never got an answer (connection refused,
        // name not resolved). A status is a server that answered no.
        HttpRequestException { StatusCode: null } => PhotoDescriptionFailure.ServerUnreachable,
        _ => PhotoDescriptionFailure.Other,
    };

    private static PhotoDescriptionResult Failed(PhotoDescriptionFailure failure) => PhotoDescriptionResult.Failed(failure);
}
