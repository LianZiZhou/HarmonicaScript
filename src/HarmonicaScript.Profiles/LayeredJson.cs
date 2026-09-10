using System.Text.Json.Nodes;

namespace HarmonicaScript.Profiles;

/// <summary>Where one effective field came from.</summary>
/// <param name="Path">JSON pointer-ish path, e.g. <c>modifiers[0].pressLeadMs</c>.</param>
/// <param name="Layer">Name of the layer that supplied the winning value.</param>
/// <param name="Value">The winning value, rendered.</param>
public readonly record struct FieldOrigin(string Path, string Layer, string Value);

/// <summary>One document in the override chain.</summary>
public sealed record JsonLayer(string Name, JsonObject Root);

/// <summary>
/// Deep-merges the override chain and records, per leaf, which layer won.
///
/// Objects merge key by key. <b>Arrays replace wholesale</b>, deliberately: merging a
/// <c>degrees</c> array element-wise would let a partial override produce an instrument that
/// nobody wrote and nobody can reason about. If you override a list, you own the whole list.
/// </summary>
public static class LayeredJson
{
    public static (JsonObject Merged, IReadOnlyList<FieldOrigin> Origins) Merge(IReadOnlyList<JsonLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
        {
            throw new ArgumentException("At least one layer is required.", nameof(layers));
        }

        var merged = new JsonObject();
        var origins = new Dictionary<string, FieldOrigin>(StringComparer.Ordinal);

        foreach (var layer in layers)
        {
            MergeInto(merged, layer.Root, layer.Name, string.Empty, origins);
        }

        return (merged, origins.Values.OrderBy(o => o.Path, StringComparer.Ordinal).ToList());
    }

    private static void MergeInto(
        JsonObject target,
        JsonObject source,
        string layerName,
        string path,
        Dictionary<string, FieldOrigin> origins)
    {
        foreach (var (key, value) in source)
        {
            // Comment keys are documentation for whoever opens the file, never data.
            if (key.StartsWith('$'))
            {
                continue;
            }

            var childPath = path.Length == 0 ? key : $"{path}.{key}";

            if (value is JsonObject childObject)
            {
                if (target[key] is not JsonObject existing)
                {
                    existing = new JsonObject();
                    target[key] = existing;
                }

                MergeInto(existing, childObject, layerName, childPath, origins);
            }
            else
            {
                target[key] = value?.DeepClone();
                origins[childPath] = new FieldOrigin(childPath, layerName, Render(value));
            }
        }
    }

    private static string Render(JsonNode? node) => node switch
    {
        null => "null",
        JsonArray a => $"[{a.Count} item(s)]",
        _ => node.ToJsonString(),
    };
}
