using System.Diagnostics;
using Deckle.Llm;

namespace Deckle.Benchmark.ModelCompare;

// ─── cache-probe: does the prompt cache pay for the batches of one dictation? ──
//
// While a person dictates, Deckle can send the text to the rewrite model in
// batches of a few sentences. Every batch carries the profile's whole system
// prompt, some two thousand tokens, for about three hundred tokens of text.
// The engine can ask the API to keep that prompt in its cache
// (PromptCacheScope.SystemPrompt); this command sends real batches through
// the production engine and prints what the API says it wrote to the cache
// and read back, so the saving is measured rather than assumed.
//
// Three orders, because the cache only serves a call that starts after the
// entry exists:
//   in-turn    each batch waits for the answer of the one before, the way
//              batches leave while someone speaks (one every 40 to 50 s);
//   together   every batch leaves at once, the way a catch-up burst does when
//              the rewriting starts late in a dictation;
//   staggered  each batch leaves a fixed delay after the one before, while
//              that one is still being answered: how long a head start the
//              first call needs before the others can read what it wrote.
//
// Each order runs on a cold cache: a random tag is put ahead of the prompt,
// since an entry is found by the request's first bytes. The tag adds about ten
// tokens to the prompt and is never the same twice, so a probe never reads
// what an earlier probe or the app itself wrote.
//
// The batch texts are written for this probe and say nothing about anyone:
// no dictation is read.

static class CacheProbe
{
    // Three stretches of about 600 characters each, the default batch size,
    // worded the way speech comes out of the transcriber.
    static readonly string[] Batches =
    [
        "Alors pour la réunion de demain je voudrais qu'on commence par le planning, parce que la dernière fois on a passé une heure sur les détails et on n'a jamais parlé des dates. Donc d'abord les dates, ensuite qui fait quoi, et seulement après les questions techniques. Je pense qu'il faut aussi prévoir un moment à la fin pour les points divers, dix minutes pas plus, sinon ça déborde toujours. Et il faudrait que quelqu'un prenne des notes, pas forcément un compte rendu complet, juste les décisions et qui s'en occupe, pour qu'on puisse les relire la semaine d'après sans se demander ce qu'on avait dit.",
        "Ensuite sur la partie livraison, euh, je crois qu'on s'est trompés dans l'ordre. On voulait livrer la page d'accueil en premier, mais en fait c'est le formulaire qui bloque tout le monde, parce que sans lui personne ne peut tester le reste. Donc je propose qu'on inverse, le formulaire d'abord, même s'il n'est pas beau, et la page d'accueil la semaine suivante. Ça veut dire qu'il faut prévenir l'équipe dès lundi, et vérifier que les textes sont prêts, parce que si les textes ne sont pas prêts on aura le même problème que le mois dernier, où tout était fini sauf les mots.",
        "Et dernier point, pour les retours des testeurs, je ne veux plus qu'ils arrivent par trois canaux différents. Là on en a par mail, dans le tableau, et certains me les disent à l'oral dans le couloir, du coup j'en oublie la moitié. On garde un seul endroit, le tableau, et si quelqu'un m'en parle à l'oral je lui demande de l'écrire. Ce n'est pas pour faire de la bureaucratie, c'est juste que sinon on corrige deux fois la même chose et on en rate une troisième. On essaie comme ça pendant un mois et on voit si ça tient, et sinon on change.",
    ];

    sealed record Call(int Batch, long StartMs, GenerationResult? Result, string? Error);

    public static int Run(string profileName, string? modelOverride, int headStartMs, string apiKey)
    {
        ProfileUnderTest profile = Corpus.LoadProfile(profileName);
        string model = modelOverride ?? profile.Model;
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException($"Profile {profileName} names no model: pass --model.");

        var engine = new ClaudeEngine(() => apiKey);
        Console.WriteLine($"profile={profileName} model={model} reasoning={profile.Reasoning?.ToString().ToLowerInvariant() ?? "auto"} prompt_chars={profile.Prompt.SystemPrompt.Length} batches={Batches.Length} batch_chars={string.Join("/", Batches.Select(b => b.Length))}");
        Console.WriteLine();

        double total = 0;
        total += Report("in-turn", model, RunOrder(engine, profile, model, delayMs: null));
        total += Report("together", model, RunOrder(engine, profile, model, delayMs: 0));
        total += Report($"staggered ({headStartMs} ms apart)", model, RunOrder(engine, profile, model, delayMs: headStartMs));

        Console.WriteLine($"probe cost: ${total:F4}");
        return 0;
    }

    // delayMs null: each batch waits for the answer before it. Otherwise
    // batch n leaves n × delayMs after the first, whatever has been answered.
    static List<Call> RunOrder(ClaudeEngine engine, ProfileUnderTest profile, string model, int? delayMs)
    {
        string prompt = $"[probe {Guid.NewGuid():N}]\n{profile.Prompt.SystemPrompt}";
        var clock = Stopwatch.StartNew();

        Call One(int index)
        {
            long start = clock.ElapsedMilliseconds;
            var request = new GenerationRequest(
                Model:        model,
                SystemPrompt: prompt,
                UserText:     Batches[index],
                Label:        "cache-probe",
                Cache:        PromptCacheScope.SystemPrompt,
                Reasoning:    profile.Reasoning);
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try
            {
                return new Call(index + 1, start, engine.Generate(request, cts.Token), null);
            }
            catch (Exception ex)
            {
                return new Call(index + 1, start, null, Transient.Describe(ex));
            }
        }

        if (delayMs is not { } delay)
            return Enumerable.Range(0, Batches.Length).Select(One).ToList();

        var tasks = Enumerable.Range(0, Batches.Length).Select(i => Task.Run(async () =>
        {
            await Task.Delay(i * delay);
            return One(i);
        })).ToArray();
        Task.WaitAll(tasks);
        return tasks.Select(t => t.Result).ToList();
    }

    // Prints one line per call and what the order cost beside the same calls
    // with no cache at all; returns the order's cost.
    static double Report(string order, string model, List<Call> calls)
    {
        Console.WriteLine(order);
        double cost = 0, uncached = 0;
        foreach (Call call in calls)
        {
            if (call.Result is not { } r)
            {
                Console.WriteLine($"  batch {call.Batch}  sent at {call.StartMs,6} ms  ERROR {call.Error}");
                continue;
            }

            // The whole prompt is the uncached input plus what was written
            // and what was read: without a cache all of it is plain input.
            int prompt = r.PromptTokens + r.CacheWriteTokens + r.CacheReadTokens;
            cost += Pricing.Cost(model, r.PromptTokens, r.EvalTokens, r.CacheWriteTokens, r.CacheReadTokens) ?? 0;
            uncached += Pricing.Cost(model, prompt, r.EvalTokens) ?? 0;
            Console.WriteLine($"  batch {call.Batch}  sent at {call.StartMs,6} ms  answered in {r.TotalMs,6} ms  prompt={prompt} (plain {r.PromptTokens}, written {r.CacheWriteTokens}, read {r.CacheReadTokens})  output={r.EvalTokens}");
        }

        string delta = uncached > 0 ? $"{(cost - uncached) / uncached:+0%;-0%}" : "n/a";
        Console.WriteLine($"  cost ${cost:F4}, without cache ${uncached:F4} ({delta})");
        Console.WriteLine();
        return cost;
    }
}
