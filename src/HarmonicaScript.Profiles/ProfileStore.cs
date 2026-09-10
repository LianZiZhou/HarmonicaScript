using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Profiles.Json;

namespace HarmonicaScript.Profiles;

/// <summary>A profile plus the record of which layer supplied each of its fields.</summary>
public sealed record Effective<T>(T Value, IReadOnlyList<FieldOrigin> Origins, IReadOnlyList<string> LayersUsed);

/// <summary>
/// The four-level override chain:
///
///   1. embedded          - compiled into this assembly; the app always works from a bare exe
///   2. app data/         - files next to the executable; what a release ships and a user edits
///   3. user data/        - %LOCALAPPDATA%\HarmonicaScript\data (or XDG on unix); survives upgrades
///   4. project snapshot  - the copy frozen inside a .hsproj, so an old project never silently re-pitches
///
/// Later layers win field by field, and every winning field's origin is recorded so
/// <c>hsc profile dump --effective</c> can show exactly where a value came from. Because layer 1
/// is embedded, a broken edit in layers 2-4 degrades to a validation error, never to a dead app.
/// </summary>
public sealed class ProfileStore
{
    private const string EmbeddedPrefix = "HarmonicaScript.Profiles.Embedded.";

    private readonly string? _appDataDir;
    private readonly string? _userDataDir;

    public ProfileStore(string? appDataDir = null, string? userDataDir = null)
    {
        _appDataDir = appDataDir ?? DefaultAppDataDir();
        _userDataDir = userDataDir ?? DefaultUserDataDir();
    }

    /// <summary>Where a release puts its editable copy of <c>data/</c>.</summary>
    public static string? DefaultAppDataDir()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "data");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        // Running from a build output: walk up to the repo's data/ so the CLI is usable in-tree.
        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var repoData = Path.Combine(dir.FullName, "data");
            if (Directory.Exists(repoData) && File.Exists(Path.Combine(repoData, "keytable.json")))
            {
                return repoData;
            }
        }

        return null;
    }

    public static string DefaultUserDataDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HarmonicaScript",
            "data");

    public Effective<InstrumentProfile> LoadInstrument(string id, JsonObject? projectSnapshot = null)
    {
        var (merged, origins, layers) = Resolve($"profiles/{id}.json", projectSnapshot);
        var dto = Deserialize(merged, ProfileJsonContext.Default.InstrumentProfileDto, id);
        var profile = ProfileFactory.ToInstrument(dto);
        return new Effective<InstrumentProfile>(profile, origins, layers);
    }

    public Effective<TimingProfile> LoadTiming(string id, JsonObject? projectSnapshot = null)
    {
        var (merged, origins, layers) = Resolve($"timing/{id}.json", projectSnapshot);
        var dto = Deserialize(merged, ProfileJsonContext.Default.TimingProfileDto, id);
        return new Effective<TimingProfile>(ProfileFactory.ToTiming(dto), origins, layers);
    }

    public Effective<GameProfileDto> LoadGame(string id, JsonObject? projectSnapshot = null)
    {
        var (merged, origins, layers) = Resolve($"games/{id}.json", projectSnapshot);
        return new Effective<GameProfileDto>(
            Deserialize(merged, ProfileJsonContext.Default.GameProfileDto, id), origins, layers);
    }

    public KeyTable LoadKeyTable(JsonObject? projectSnapshot = null)
    {
        var (merged, _, _) = Resolve("keytable.json", projectSnapshot);
        return KeyTable.FromDto(Deserialize(merged, ProfileJsonContext.Default.KeyTableDto, "keytable"));
    }

    /// <summary>Loads instrument + timing + key table together and validates them as a set.</summary>
    public LoadedInstrument LoadValidated(string instrumentId, string timingId, JsonObject? snapshot = null)
    {
        var instrument = LoadInstrument(instrumentId, snapshot);
        var timing = LoadTiming(timingId, snapshot);
        var keys = LoadKeyTable(snapshot);

        var problems = ProfileValidator.Validate(instrument.Value, timing.Value)
            .Concat(keys.ValidateBindings(instrument.Value))
            .ToList();

        if (problems.Count > 0)
        {
            throw new ProfileValidationException(instrumentId, problems);
        }

        return new LoadedInstrument(
            instrument.Value,
            timing.Value,
            keys,
            EmissionTable.Build(instrument.Value),
            [.. instrument.Origins, .. timing.Origins],
            [.. instrument.LayersUsed.Concat(timing.LayersUsed).Distinct()]);
    }

    private (JsonObject Merged, IReadOnlyList<FieldOrigin> Origins, IReadOnlyList<string> Layers) Resolve(
        string relativePath,
        JsonObject? projectSnapshot)
    {
        var layers = new List<JsonLayer>();

        if (ReadEmbedded(relativePath) is { } embedded)
        {
            layers.Add(new JsonLayer("embedded", embedded));
        }

        if (_appDataDir is not null && ReadFile(Path.Combine(_appDataDir, ToNativePath(relativePath))) is { } app)
        {
            layers.Add(new JsonLayer($"app:{_appDataDir}", app));
        }

        if (_userDataDir is not null && ReadFile(Path.Combine(_userDataDir, ToNativePath(relativePath))) is { } user)
        {
            layers.Add(new JsonLayer($"user:{_userDataDir}", user));
        }

        if (projectSnapshot is not null)
        {
            layers.Add(new JsonLayer("project", projectSnapshot));
        }

        if (layers.Count == 0)
        {
            throw new FileNotFoundException(
                $"No layer supplies '{relativePath}'. Looked in the embedded resources, "
                + $"'{_appDataDir ?? "(no app data dir)"}' and '{_userDataDir ?? "(no user data dir)"}'.");
        }

        var (merged, origins) = LayeredJson.Merge(layers);
        return (merged, origins, layers.Select(l => l.Name).ToList());
    }

    private static string ToNativePath(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar);

    private static JsonObject? ReadEmbedded(string relativePath)
    {
        var resourceName = EmbeddedPrefix + relativePath.Replace('/', '.');
        var assembly = typeof(ProfileStore).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), resourceName);
    }

    private static JsonObject? ReadFile(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path), path) : null;

    private static JsonObject Parse(string json, string what)
    {
        var node = JsonNode.Parse(
            json,
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        return node as JsonObject
            ?? throw new InvalidOperationException($"'{what}' does not contain a JSON object at its root.");
    }

    private static T Deserialize<T>(JsonObject merged, JsonTypeInfo<T> typeInfo, string what) =>
        merged.Deserialize(typeInfo)
        ?? throw new InvalidOperationException($"'{what}' deserialised to null.");
}

/// <summary>Everything the engine needs about one instrument, loaded and cross-validated.</summary>
public sealed record LoadedInstrument(
    InstrumentProfile Instrument,
    TimingProfile Timing,
    KeyTable Keys,
    EmissionTable Emission,
    IReadOnlyList<FieldOrigin> Origins,
    IReadOnlyList<string> LayersUsed);
