using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HarmonicaScript.App.I18n;
using HarmonicaScript.Core.Conversion;
using HarmonicaScript.Core.Timeline;
using HarmonicaScript.Export;
using HarmonicaScript.Project;

namespace HarmonicaScript.App.ViewModels;

public sealed partial class TrackRow : ObservableObject
{
    [ObservableProperty] private bool _selected;

    public required int Index { get; init; }

    public required string Name { get; init; }

    public required int NoteCount { get; init; }

    public required string Duration { get; init; }

    public required string Range { get; init; }

    public required double Polyphony { get; init; }

    public required double MelodyScore { get; init; }

    public required bool IsPercussion { get; init; }

    public bool IsSelectable => !IsPercussion;
}

public sealed record NoteRow(
    int Id,
    string Time,
    string Degree,
    string Sharp,
    string Octave,
    string Key,
    string Modifiers,
    int SourceMidiNote,
    int Delta,
    string Flags,
    bool HasProblem);

public sealed record PenaltySegment(string Reason, int Penalty, double Fraction)
{
    /// <summary>Pixel width of this segment in the stacked bar. Computed here so the view needs no converter.</summary>
    public double BarWidth => Math.Max(28, Fraction * 420);
}

public sealed record CandidateRow(int Transpose, int Infeasible, string Loss, int KeyDistance, long Mechanical, bool IsChosen);

public sealed record TimingRow(string Name, int Value, string Source, bool Measured);

/// <summary>
/// The single window's state. A plain testable object: it holds no Avalonia types, so its logic
/// is exercised by ordinary unit tests rather than by driving a UI.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ConversionService _service = new();
    private ConversionService.Outcome? _outcome;

    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private bool _hasFile;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _profileVerified;

    [ObservableProperty] private int _transpose;
    [ObservableProperty] private string _transposeMode = "Balanced";
    [ObservableProperty] private double _speed = 1.0;
    [ObservableProperty] private string _reduction = "Highest";
    [ObservableProperty] private string _outOfRange = "FoldThenDrop";
    [ObservableProperty] private string _timingProfile = "df.reference";

    [ObservableProperty] private string _grade = "-";
    [ObservableProperty] private int _playability;
    [ObservableProperty] private string _keptSummary = string.Empty;
    [ObservableProperty] private string _ceilingSummary = string.Empty;
    [ObservableProperty] private bool _onlyProblems = true;
    [ObservableProperty] private string _exportTarget = "ahk-v2";
    [ObservableProperty] private string _exportMessage = string.Empty;

    public Loc L => Loc.Instance;

    public ObservableCollection<TrackRow> Tracks { get; } = [];

    public ObservableCollection<NoteRow> Notes { get; } = [];

    public ObservableCollection<PenaltySegment> Penalties { get; } = [];

    public ObservableCollection<CandidateRow> Candidates { get; } = [];

    public ObservableCollection<TimingRow> TimingValues { get; } = [];

    public IReadOnlyList<string> TransposeModes { get; } = ["PreserveKey", "Balanced", "Free", "Manual"];

    public IReadOnlyList<string> Reductions { get; } = ["Highest", "Lowest", "Loudest", "TrackPriority"];

    public IReadOnlyList<string> OutOfRangeModes { get; } = ["FoldThenDrop", "DropOnly", "FoldUnbounded"];

    public IReadOnlyList<string> TimingProfiles { get; } = ["df.reference", "df.conservative"];

    public IReadOnlyList<string> ExportTargets { get; } =
        [.. ConversionService.BuildRegistry().All.Select(e => e.Id)];

    public ConversionService.Outcome? Outcome => _outcome;

    [RelayCommand]
    public void ToggleLanguage() => Loc.Instance.Toggle();

    public void Load(string path)
    {
        FileName = Path.GetFileName(path);
        _pendingPath = path;
        Reconvert();
        HasFile = true;
    }

    private string? _pendingPath;

    /// <summary>
    /// Re-runs the whole pipeline. Cheap enough to call on every settings change - the search is
    /// tens of milliseconds - which is what makes the transposition candidate list clickable.
    /// </summary>
    public void Reconvert()
    {
        if (_pendingPath is null)
        {
            return;
        }

        try
        {
            var settings = new ConversionSettings
            {
                Speed = Speed,
                Reduction = Enum.Parse<ReductionPolicy>(Reduction),
                OutOfRange = Enum.Parse<OutOfRangePolicy>(OutOfRange),
                TranspositionMode = Enum.Parse<TranspositionMode>(TransposeMode),
                ManualTranspose = Transpose,
                SelectedTracks = [.. Tracks.Where(t => t.Selected).Select(t => t.Index)],
            };

            var profiles = _service.LoadProfiles("df.harmonica.v1", TimingProfile);
            _outcome = _service.Convert(_pendingPath, settings, profiles);

            RefreshTracks();
            RefreshReport();
            RefreshNotes();
            RefreshTiming();
            Status = string.Empty;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    private void RefreshTracks()
    {
        if (_outcome is null || Tracks.Count > 0)
        {
            return;
        }

        var auto = _outcome.Song.AutoSelectedTrack;
        foreach (var track in _outcome.Song.Tracks.Where(t => t.NoteCount > 0))
        {
            Tracks.Add(new TrackRow
            {
                Index = track.Index,
                Name = track.Name ?? $"#{track.Index}",
                NoteCount = track.NoteCount,
                Duration = TimeSpan.FromMicroseconds(track.DurationUs).ToString(@"m\:ss", CultureInfo.InvariantCulture),
                Range = $"{track.MinPitch}-{track.MaxPitch}",
                Polyphony = Math.Round(track.MeanPolyphony, 2),
                MelodyScore = Math.Round(track.MelodyScore, 3),
                IsPercussion = track.IsPercussion,
                Selected = track.Index == auto,
            });
        }
    }

    private void RefreshReport()
    {
        if (_outcome is null)
        {
            return;
        }

        var report = _outcome.Conversion.Score.Report;
        ProfileVerified = _outcome.Profiles.Instrument.Provenance.VerifiedInGame;

        Grade = report.Grade;
        Playability = report.PlayabilityScore;
        Transpose = _outcome.Conversion.Score.Transpose;

        KeptSummary = $"{report.AudibleNoteCount} / {report.SourceNoteCount} ({report.PreservedFraction * 100:0.0}%)";
        CeilingSummary =
            $"same key {report.SameKeyCeilingNps:0.0} · key change {report.KeyChangeCeilingNps:0.0} · "
            + $"modifier swap {report.ModifierSwapCeilingNps:0.0} · effective {report.EffectiveCeilingNps:0.0} "
            + $"(this score P95 {report.P95Nps:0.0})";

        Penalties.Clear();
        var breakdown = report.PenaltyBreakdown().Where(p => p.Penalty > 0).ToList();
        var total = Math.Max(1, breakdown.Sum(p => p.Penalty));
        foreach (var (reason, penalty) in breakdown)
        {
            Penalties.Add(new PenaltySegment(reason, penalty, (double)penalty / total));
        }

        Candidates.Clear();
        foreach (var candidate in report.TopCandidates)
        {
            Candidates.Add(new CandidateRow(
                candidate.Transpose,
                candidate.HardInfeasible,
                (candidate.MusicalLossPpm / 10_000.0).ToString("0.00", CultureInfo.InvariantCulture) + "%",
                candidate.KeyDistance,
                candidate.Mechanical / 1_000_000,
                candidate.Transpose == _outcome.Conversion.Score.Transpose));
        }
    }

    private void RefreshTiming()
    {
        if (_outcome is null)
        {
            return;
        }

        var t = _outcome.Profiles.Timing;
        TimingValues.Clear();
        foreach (var (name, value) in new (string, Core.Instrument.TimingValue)[]
        {
            ("sameKeyRetriggerMs", t.SameKeyRetriggerMs),
            ("keyChangeGapMs", t.KeyChangeGapMs),
            ("noteHoldMinMs", t.NoteHoldMinMs),
            ("globalLeadMs", t.GlobalLeadMs),
            ("exclusiveSwapMs", t.ExclusiveSwapMs),
            ("maxOnsetShiftMs", t.MaxOnsetShiftMs),
            ("maxKeyHoldMs", t.MaxKeyHoldMs),
        })
        {
            TimingValues.Add(new TimingRow(name, value.V, value.Source, value.Measured));
        }
    }

    partial void OnOnlyProblemsChanged(bool value) => RefreshNotes();

    private void RefreshNotes()
    {
        Notes.Clear();
        if (_outcome is null)
        {
            return;
        }

        var profile = _outcome.Profiles.Instrument;
        var emission = _outcome.Profiles.Emission;

        foreach (var note in _outcome.Conversion.Score.Notes)
        {
            var hasProblem = note.Alteration != NoteAlteration.None;
            if (OnlyProblems && !hasProblem)
            {
                continue;
            }

            var degree = note.Fingering is { } f ? profile.Degrees[f.DegreeIndex] : null;
            var mask = note.Fingering is { } g ? emission.States[g.StateIndex].Mask : (ushort)0;
            var modifiers = mask == 0
                ? "-"
                : string.Join('+', profile.Modifiers.Where((_, i) => (mask & (1 << i)) != 0).Select(m => m.DisplayName.ZhHans));

            Notes.Add(new NoteRow(
                note.Id,
                TimeSpan.FromMicroseconds(note.ScheduledDownUs).ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture),
                degree?.Jianpu ?? "-",
                degree is not null && degree.Jianpu.StartsWith('#') ? "#" : string.Empty,
                note.Fingering is { } h ? emission.States[h.StateIndex].SemitoneDelta.ToString("+#;-#;0", CultureInfo.InvariantCulture) : "-",
                degree is null ? "-" : _outcome.Profiles.Keys.Describe(degree.Binding),
                modifiers,
                note.SourceMidiNote,
                note.SemitoneError,
                note.Alteration == NoteAlteration.None ? string.Empty : note.Alteration.ToString(),
                hasProblem));
        }
    }

    [RelayCommand]
    public void Export()
    {
        if (_outcome is null)
        {
            return;
        }

        var exporter = ConversionService.BuildRegistry().ById(ExportTarget);
        if (exporter is null)
        {
            ExportMessage = $"unknown target {ExportTarget}";
            return;
        }

        var stem = Path.GetFileNameWithoutExtension(FileName);
        var path = Path.Combine(Path.GetDirectoryName(_pendingPath!)!, stem + exporter.FileExtension);

        using var stream = File.Create(path);
        var result = exporter.Write(ConversionService.BuildExportContext(_outcome, stem), stream);

        ExportMessage = result.Ok
            ? $"{path}\n{string.Join('\n', result.ExtraInstructions)}"
            : $"{L["Export_Refused"]}: {string.Join("; ", result.Warnings.Select(w => w.Detail))}";
    }
}
