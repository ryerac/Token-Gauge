using System.Globalization;
using System.Text.Json;

namespace TokenGauge;

/// <summary>Tolerant readers for undocumented JSON: missing or oddly typed fields come back as null.</summary>
internal static class Json
{
    public static string? Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    public static double? Num(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;

    public static JsonElement? Obj(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
            ? v
            : null;

    /// <summary>Reads an ISO-8601 string or a Unix timestamp in seconds.</summary>
    public static DateTimeOffset? Time(JsonElement o, string name)
    {
        if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var secs) && secs > 0)
            return DateTimeOffset.FromUnixTimeSeconds(secs);
        if (v.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t))
            return t;
        return null;
    }

    /// <summary>Depth-first search for the first object-valued property with the given name.</summary>
    public static JsonElement? FindObject(JsonElement e, string name)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.NameEquals(name) && p.Value.ValueKind == JsonValueKind.Object) return p.Value;
                    if (FindObject(p.Value, name) is { } found) return found;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    if (FindObject(item, name) is { } found) return found;
                break;
        }
        return null;
    }

    /// <summary>Reads a file that another program may be writing to at the same time.</summary>
    public static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }
}
