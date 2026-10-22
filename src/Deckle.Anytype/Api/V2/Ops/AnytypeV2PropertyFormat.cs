namespace Deckle.Anytype;

// The value formats a type property can have, as add_property's format enum
// serves them. Only needed when the op creates the property; on one that
// exists the format must match what it already has.
public enum AnytypeV2PropertyFormat
{
    Text,
    Number,
    Select,
    MultiSelect,
    Date,
    Files,
    Checkbox,
    Url,
    Email,
    Phone,
    Objects,
}

internal static class AnytypeV2PropertyFormatWire
{
    internal static string ToWire(this AnytypeV2PropertyFormat format) => format switch
    {
        AnytypeV2PropertyFormat.Text => "text",
        AnytypeV2PropertyFormat.Number => "number",
        AnytypeV2PropertyFormat.Select => "select",
        AnytypeV2PropertyFormat.MultiSelect => "multi_select",
        AnytypeV2PropertyFormat.Date => "date",
        AnytypeV2PropertyFormat.Files => "files",
        AnytypeV2PropertyFormat.Checkbox => "checkbox",
        AnytypeV2PropertyFormat.Url => "url",
        AnytypeV2PropertyFormat.Email => "email",
        AnytypeV2PropertyFormat.Phone => "phone",
        AnytypeV2PropertyFormat.Objects => "objects",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };
}
