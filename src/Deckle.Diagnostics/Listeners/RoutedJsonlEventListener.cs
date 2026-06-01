using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Deckle.Diagnostics.Listeners;

// Variante routée du JsonlEventListener. Identique en posture (un
// listener, un predicate, un kindLabel, sérialisation JSON ligne-par-
// événement, file lock pour éviter le tearing), sauf que la destination
// n'est plus un chemin figé à l'instanciation : un `pathResolver` la
// calcule par événement à partir de son EventEntry. Permet à un seul
// listener de pulvériser un même flux d'events vers une arborescence
// dynamique de `corpus.jsonl` bucketés (par exemple
// `corpus/raw/<tier>/corpus.jsonl` ou `corpus/rewrite-<name>-<id>/corpus.jsonl`
// — voir ADR-0006).
//
// Pourquoi pas l'héritage du JsonlEventListener. Le brief de la refonte
// corpus a tranché : pas d'héritage. Le mode "routé" n'est pas un
// surcomportement du mode "plat" — c'est une autre stratégie de
// destination. Exposer un mode mutable côté listener générique rendrait
// l'API plus fragile pour zéro gain (les deux types portent une
// poignée de lignes en commun et leur duplication contrôlée évite de
// coupler leurs cycles d'évolution).
//
// Concurrence. Plusieurs paths résolus simultanément peuvent atterrir
// sur des fichiers différents — un lock global sérialiserait des
// écritures qui n'ont aucune raison de se bloquer mutuellement. Le
// listener tient un `ConcurrentDictionary<string, object>` indexé par
// path concret : chaque path a son propre lock, alloué paresseusement
// au premier event qui y écrit. La création du dossier parent
// piggyback sur ce même `GetOrAdd` — premier event d'un path crée
// `Directory.CreateDirectory` une seule fois.
//
// Sécurité. Aucune validation des composants du path ici — c'est la
// responsabilité du producer (ou du resolver) d'avoir sanitizé les
// segments dynamiques avant qu'ils ne traversent. `CorpusPaths.Sanitize`
// est l'utilitaire prévu à cet effet côté producer.
public sealed class RoutedJsonlEventListener : EventListener
{
    private readonly Func<EventEntry, string> _pathResolver;
    private readonly Func<EventEntry, bool> _predicate;
    private readonly string _kindLabel;
    private readonly ConcurrentDictionary<string, object> _pathLocks = new();
    private readonly List<EventSource> _earlySources = new();
    private bool _ready;

    private static readonly JsonWriterOptions _jsonOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // `pathResolver` — calcule la destination absolue à partir de
    //                  l'EventEntry. Appelé pour chaque event qui passe
    //                  le predicate. Doit retourner un chemin de fichier
    //                  absolu et déjà sanitizé ; un retour null/vide
    //                  fait silencieusement skiper l'event.
    // `kindLabel`    — valeur écrite sous la clé "kind" du JSONL.
    //                  Aligné sur les labels du JsonlEventListener
    //                  classique ("log", "latency", …).
    // `predicate`    — sélectionne quels events atterrissent dans ce
    //                  listener. Reçoit l'EventEntry complète pour
    //                  filtrer sur nom, niveau, keywords ou payload.
    public RoutedJsonlEventListener(
        Func<EventEntry, string> pathResolver,
        string kindLabel,
        Func<EventEntry, bool> predicate)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _kindLabel = kindLabel;
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));

        lock (_earlySources)
        {
            _ready = true;
            foreach (var src in _earlySources)
                EnableEvents(src, EventLevel.LogAlways, EventKeywords.All);
            _earlySources.Clear();
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name is null) return;
        if (!eventSource.Name.StartsWith("Deckle.", StringComparison.Ordinal)) return;

        lock (_earlySources)
        {
            if (!_ready)
            {
                _earlySources.Add(eventSource);
                return;
            }
        }
        EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.All);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        var entry = LogWindowEventListener.BuildEntry(eventData);
        if (!_predicate(entry)) return;

        string path;
        try { path = _pathResolver(entry); }
        catch { return; }
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            WriteLine(path, entry);
        }
        catch
        {
            // Posture identique au JsonlEventListener : une I/O qui
            // échoue ne doit pas faire crasher l'émetteur. Surfacer
            // ce genre d'erreur (compteur, event dédié) est un futur
            // chantier observabilité.
        }
    }

    private void WriteLine(string path, EventEntry entry)
    {
        byte[] jsonBytes;
        using (var ms = new MemoryStream(capacity: 256))
        {
            using (var writer = new Utf8JsonWriter(ms, _jsonOptions))
            {
                writer.WriteStartObject();
                writer.WriteString("timestamp", entry.Timestamp.ToString("o", CultureInfo.InvariantCulture));
                writer.WriteString("kind", _kindLabel);
                writer.WriteString("session", DeckleEventSource.SessionId);
                writer.WritePropertyName("payload");
                writer.WriteStartObject();
                foreach (var kv in entry.Payload)
                    WriteValue(writer, kv.Key, kv.Value);
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.Flush();
            }
            jsonBytes = ms.ToArray();
        }

        // Lock par path résolu — l'allocation paresseuse via GetOrAdd
        // garantit qu'un même path est toujours associé au même
        // object, et donc qu'un seul thread écrit à la fois dans ce
        // fichier. Le delta création du dossier parent vit dans la
        // factory du GetOrAdd : premier event d'un path crée
        // Directory.CreateDirectory une fois, jamais re-vérifié.
        object lockObj = _pathLocks.GetOrAdd(path, p =>
        {
            string? parent = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            return new object();
        });

        lock (lockObj)
        {
            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            fs.Write(jsonBytes, 0, jsonBytes.Length);
            fs.WriteByte((byte)'\n');
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, object? value)
    {
        switch (value)
        {
            case null:                       writer.WriteNull(name); break;
            case string s:                   writer.WriteString(name, s); break;
            case bool b:                     writer.WriteBoolean(name, b); break;
            case int i:                      writer.WriteNumber(name, i); break;
            case long l:                     writer.WriteNumber(name, l); break;
            case short sh:                   writer.WriteNumber(name, sh); break;
            case byte by:                    writer.WriteNumber(name, by); break;
            case uint ui:                    writer.WriteNumber(name, ui); break;
            case ulong ul:                   writer.WriteNumber(name, ul); break;
            case ushort us:                  writer.WriteNumber(name, us); break;
            case sbyte sb:                   writer.WriteNumber(name, sb); break;
            case float f:                    writer.WriteNumber(name, f); break;
            case double d:                   writer.WriteNumber(name, d); break;
            case Guid g:                     writer.WriteString(name, g.ToString()); break;
            case DateTime dt:                writer.WriteString(name, dt.ToString("o", CultureInfo.InvariantCulture)); break;
            case DateTimeOffset dto:         writer.WriteString(name, dto.ToString("o", CultureInfo.InvariantCulture)); break;
            // Fallback : EventSource ne permet qu'un set restreint de
            // primitives dans les signatures [Event], donc on ne devrait
            // jamais atteindre cette branche.
            default:                         writer.WriteString(name, value.ToString() ?? string.Empty); break;
        }
    }
}
