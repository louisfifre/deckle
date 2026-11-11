using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// The actors of the model v6: a person and a company, created in two gestures
// (a person never needs a company), then linked from the work they took part
// in with link. Both carry the shared coordinates: email, phone, website and
// address.
//
// The role is a select whose options the person creates in Anytype, never this
// code: it is looked for live, and a person asked with a role the space cannot
// hold is still created, with the answer saying so.
//
// Both creations go through the v2 writer, whose Idempotency-Key keeps a
// resend from making a second person. The property values are plain strings:
// the email and phone formats are not measured on a v2 create.
public sealed class ContactGestures(AnytypeApiClient api, NameResolver resolver, AnytypeV2Client v2)
{
    readonly AnytypeV2ObjectReader _reader = new(v2);
    readonly AnytypeV2Writer _writer = new(v2);

    public async Task<string> CreatePersonAsync(
        string name, string? email = null, string? phone = null, string? company = null,
        string? role = null, string? website = null, string? address = null, CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();
        name = RequireName(name);

        // Every read and refusal before the create.
        string? companyId = null, companyName = null;
        if (!string.IsNullOrWhiteSpace(company))
        {
            companyId = await resolver.ResolveAsync(company, [DevSpace.Types.Company], NameMatch.Exact, ct);
            companyName = await NameAsync(companyId, company, ct);
        }

        string? roleKey = null, roleName = null, roleNote = null;
        if (!string.IsNullOrWhiteSpace(role))
        {
            LiveSelect? live = await LiveSelect.FindAsync(api, DevSpace.Role.Keys, DevSpace.Role.Name, ct);
            if (live is null)
                roleNote = $"Rôle : non posé, l'espace n'a pas encore de propriété « {DevSpace.Role.Name} » à options.";
            else
            {
                roleKey = live.Key;
                roleName = live.OptionName(role) ?? throw live.Unknown(role, nameof(role));
            }
        }

        var props = new JsonObject();
        if (Clean(email) is { } mail) props[DevSpace.Props.Email] = mail;
        if (Clean(phone) is { } tel) props[DevSpace.Props.Phone] = tel;
        if (Clean(website) is { } site) props[DevSpace.Props.Website] = site;
        if (Clean(address) is { } where) props[DevSpace.Props.Address] = where;
        if (companyId is not null) props[DevSpace.Props.Company] = new JsonArray(companyId);
        if (roleKey is not null && roleName is not null) props[roleKey] = new JsonArray(roleName);

        string id = await CreateAsync(DevSpace.Types.Person, name, props, ct);

        var facts = new List<string> { $"Personne créée : {name}", $"id : {id}" };
        if (companyName is not null) facts.Add($"Entreprise : {companyName}");
        if (roleName is not null) facts.Add($"Rôle : {roleName}");
        if (roleNote is not null) facts.Add(roleNote);
        DeckleAnytypeSource.Log.GestureCompleted("create_person", Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
        return string.Join('\n', facts);
    }

    public async Task<string> CreateCompanyAsync(
        string name, string? address = null, string? email = null, string? phone = null,
        string? website = null, CancellationToken ct = default)
    {
        long t0 = Stopwatch.GetTimestamp();
        name = RequireName(name);

        var props = new JsonObject();
        if (Clean(address) is { } where) props[DevSpace.Props.Address] = where;
        if (Clean(email) is { } mail) props[DevSpace.Props.Email] = mail;
        if (Clean(phone) is { } tel) props[DevSpace.Props.Phone] = tel;
        if (Clean(website) is { } site) props[DevSpace.Props.Website] = site;

        string id = await CreateAsync(DevSpace.Types.Company, name, props, ct);

        DeckleAnytypeSource.Log.GestureCompleted("create_company", Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
        return $"Entreprise créée : {name}\nid : {id}";
    }

    // The id comes back at the root of the receipt (measured on open_report).
    // It is answered because a fresh object is not found by its name until
    // the search index catches up (some twenty seconds, seen on 2026-11-09),
    // and the next gesture is often a link to it.
    async Task<string> CreateAsync(string type, string name, JsonObject props, CancellationToken ct)
    {
        var payload = new JsonObject { ["type"] = type, ["name"] = name };
        if (props.Count > 0) payload["properties"] = props;
        AnytypeV2WriteReceipt receipt = await _writer.CreateObjectAsync(payload, ct: ct);
        return receipt.Body?["id"] is JsonValue value && value.TryGetValue(out string? id) && !string.IsNullOrEmpty(id)
            ? id
            : "(non renvoyé)";
    }

    async Task<string> NameAsync(string id, string fallback, CancellationToken ct)
    {
        AnytypeV2ObjectDocument doc = await _reader.ReadAsync(
            id, new AnytypeV2ObjectReadOptions { Parts = AnytypeV2ObjectParts.Properties }, ct);
        string name = AnytypeV2PropertyMap.Of(doc).Name;
        return name.Length > 0 ? name : fallback;
    }

    static string RequireName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Le nom ne peut pas être vide.", nameof(name))
            : name.Trim();

    static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
