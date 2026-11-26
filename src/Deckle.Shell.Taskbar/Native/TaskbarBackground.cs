namespace Deckle.Shell.Taskbar;

/// <summary>What the taskbar's background is drawn as.</summary>
public enum TaskbarBackground
{
    /// <summary>Explorer's own acrylic; Deckle leaves it alone.</summary>
    Native,

    /// <summary>No fill: the wallpaper shows through.</summary>
    Transparent,
}

// Reads the background by its name. "black", a choice removed in v0.157
// (it dated from the black band over the taskbar, which Taskbar fade
// replaced), reads as transparent rather than failing the whole file; any
// other unknown name reads as the default.
internal sealed class TaskbarBackgroundJsonConverter : System.Text.Json.Serialization.JsonConverter<TaskbarBackground>
{
    public override TaskbarBackground Read(
        ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        => reader.GetString()?.ToLowerInvariant() switch
        {
            "transparent" or "black" => TaskbarBackground.Transparent,
            _                        => TaskbarBackground.Native,
        };

    public override void Write(
        System.Text.Json.Utf8JsonWriter writer, TaskbarBackground value, System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(value == TaskbarBackground.Transparent ? "transparent" : "native");
}
