using System.Text.Json.Nodes;

namespace Deckle.Anytype;

internal sealed record TypeIconSpec(
    string Format,
    string? Name,
    string? Color,
    string? Emoji)
{
    public string Display => Format switch
    {
        "icon" when Color is not null => $"icon:{Name}:{Color}",
        "icon" => $"icon:{Name}",
        _ => $"emoji:{Emoji}",
    };

    public JsonObject ToPayload()
    {
        var payload = new JsonObject { ["format"] = Format };
        if (Format == "icon")
        {
            payload["name"] = Name;
            if (Color is not null)
                payload["color"] = Color;
        }
        else
        {
            payload["emoji"] = Emoji;
        }
        return payload;
    }

    public SchemaTypeIconInfo ToInfo() => new(Format, Name, Color, Emoji, null);

    // The icon already on the type is the one asked for. A file icon never
    // matches: a manifest cannot express one.
    public bool Matches(SchemaTypeIconInfo existing) =>
        string.Equals(existing.Format, Format, StringComparison.Ordinal)
        && string.Equals(existing.Name, Name, StringComparison.Ordinal)
        && string.Equals(existing.Color, Color, StringComparison.Ordinal)
        && string.Equals(existing.Emoji, Emoji, StringComparison.Ordinal);

    public static TypeIconSpec Parse(JsonObject obj, string typeKey) =>
        Parse(obj, $"type {typeKey}", $"le type « {typeKey} »");

    // owner names the JSON location for shape errors ("type piece"), subject is
    // the French phrase the corrective message points at ("le type « piece »").
    // Sections reuse the same icon grammar with their own phrasing.
    public static TypeIconSpec Parse(JsonObject obj, string owner, string subject)
    {
        string format = SchemaManifestFields.RequiredString(obj, "format", rejectNonString: true);
        if (format == "icon")
        {
            JsonShape.RequireOnly(obj, ["format", "name", "color"], $"{owner}.icon");
            string name = SchemaManifestFields.RequiredString(obj, "name", rejectNonString: true);
            if (!AnytypeTypeIconCatalog.Names.Contains(name))
                throw new ArgumentException(
                    $"Nom d’icône Anytype inconnu « {name} » pour {subject}. " +
                    "Utilise un nom du catalogue built-in de l’API 2025-05-20.");

            string? color = SchemaManifestFields.OptionalString(obj, "color", rejectNonString: true);
            if (color is not null && !AnytypeTypeIconCatalog.Colors.Contains(color))
                throw new ArgumentException(
                    $"Couleur d’icône Anytype inconnue « {color} » pour {subject}. " +
                    $"Couleurs acceptées : {string.Join(", ", AnytypeTypeIconCatalog.Colors)}.");

            return new TypeIconSpec(format, name, color, null);
        }

        if (format == "emoji")
        {
            JsonShape.RequireOnly(obj, ["format", "emoji"], $"{owner}.icon");
            string emoji = SchemaManifestFields.RequiredString(obj, "emoji", rejectNonString: true);
            return new TypeIconSpec(format, null, null, emoji);
        }

        throw new ArgumentException(
            $"Format d’icône inconnu « {format} » pour {subject}. " +
            "Formats acceptés : icon, emoji.");
    }
}

internal static class AnytypeTypeIconCatalog
{
    // anytype-heart v0.41.0, the implementation backing API 2025-05-20.
    internal static readonly IReadOnlySet<string> Names = new HashSet<string>(
        """
        accessibility add-circle airplane alarm albums alert-circle american-football analytics aperture apps archive arrow-back-circle arrow-down-circle arrow-forward-circle arrow-redo-circle arrow-redo arrow-undo-circle arrow-undo arrow-up-circle at-circle attach backspace bag-add bag-check bag-handle bag-remove bag balloon ban bandage bar-chart barbell barcode baseball basket basketball battery-charging battery-dead battery-full battery-half beaker bed beer bicycle binoculars bluetooth boat body bonfire book bookmark bookmarks bowling-ball briefcase browsers brush bug build bulb bus business cafe calculator calendar-clear calendar-number calendar call camera-reverse camera car-sport car card caret-back-circle caret-back caret-down-circle caret-down caret-forward-circle caret-forward caret-up-circle caret-up cart cash cellular chatbox-ellipses chatbox chatbubble-ellipses chatbubble chatbubbles checkbox checkmark-circle checkmark-done-circle chevron-back-circle chevron-down-circle chevron-forward-circle chevron-up-circle clipboard close-circle cloud-circle cloud-done cloud-download cloud-offline cloud-upload cloud cloudy-night cloudy code-slash code cog color-fill color-filter color-palette color-wand compass construct contact contract contrast copy create crop cube cut desktop diamond dice disc document-attach document-lock document-text document documents download duplicate ear earth easel egg ellipse ellipsis-horizontal-circle ellipsis-vertical-circle enter exit expand extension-puzzle eye-off eye eyedrop fast-food female file-tray-full file-tray-stacked file-tray film filter-circle finger-print fish fitness flag flame flash-off flash flashlight flask flower folder-open folder football footsteps funnel game-controller gift git-branch git-commit git-compare git-merge git-network git-pull-request glasses globe golf grid hammer hand-left hand-right happy hardware-chip headset heart-circle heart-dislike-circle heart-dislike heart-half heart help-buoy help-circle home hourglass ice-cream id-card image images infinite information-circle invert-mode journal key keypad language laptop layers leaf library link list-circle list locate location lock-closed lock-open log-in log-out logo-alipay logo-amazon logo-amplify logo-android magnet mail-open mail-unread mail male-female male man map medal medical medkit megaphone menu mic-circle mic-off-circle mic-off mic moon move musical-note musical-notes navigate-circle navigate newspaper notifications-circle notifications-off-circle notifications-off notifications nuclear nutrition options paper-plane partly-sunny pause-circle pause paw pencil people-circle people person-add person-circle person-remove person phone-landscape phone-portrait pie-chart pin pint pizza planet play-back-circle play-back play-circle play-forward-circle play-forward play-skip-back-circle play-skip-back play-skip-forward-circle play-skip-forward play podium power pricetag pricetags print prism pulse push qr-code radio-button-off radio-button-on radio rainy reader receipt recording refresh-circle refresh reload-circle reload remove-circle repeat resize restaurant ribbon rocket rose sad save scale scan-circle scan school search-circle search send server settings shapes share-social share shield-checkmark shield-half shield shirt shuffle skull snow sparkles speedometer square star-half star stats-chart stop-circle stop stopwatch storefront subway sunny swap-horizontal swap-vertical sync-circle sync tablet-landscape tablet-portrait telescope tennisball terminal text thermometer thumbs-down thumbs-up thunderstorm ticket time timer today toggle trail-sign train transgender trash-bin trash trending-down trending-up triangle trophy tv umbrella unlink videocam-off videocam volume-high volume-low volume-medium volume-mute volume-off walk wallet warning watch water wifi wine woman
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries),
        StringComparer.Ordinal);

    internal static readonly IReadOnlySet<string> Colors = new HashSet<string>(
        ["grey", "yellow", "orange", "red", "pink", "purple", "blue", "ice", "teal", "lime"],
        StringComparer.Ordinal);
}
