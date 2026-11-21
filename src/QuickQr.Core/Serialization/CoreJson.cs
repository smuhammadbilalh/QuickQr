using System.Text.Json;
using System.Text.Json.Serialization;
using QuickQr.Core.Rendering;
using QuickQr.Core.Settings;

namespace QuickQr.Core.Serialization;

/// <summary>Trim/AOT-safe JSON for Core types (source generated).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(QrStyle))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<BrandKit>))]
public sealed partial class CoreJsonContext : JsonSerializerContext;

public static class CoreJson
{
    /// <summary>Serialises a style without logo bytes (logos are stored separately to keep history rows small).</summary>
    public static string SerializeStyle(QrStyle style) =>
        JsonSerializer.Serialize(style with { Logo = null }, CoreJsonContext.Default.QrStyle);

    public static QrStyle? DeserializeStyle(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize(json, CoreJsonContext.Default.QrStyle); }
        catch (JsonException) { return null; }
    }
}
