using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Core.Planning;
using HarmonicaScript.Core.Source;

namespace HarmonicaScript.Core.Conversion;

/// <summary>
/// A named counter emitted by a pass. Pass ids are STABLE SEMANTIC STRINGS WITH NO ORDINAL
/// (<c>reduce.dropShort</c>, not <c>P08.DropTooShort</c>): inserting a pass in a later version
/// must not churn thirty golden report files.
/// </summary>
public readonly record struct PassCounter(string Id, long Value);

public sealed record PassResult(bool Changed, IReadOnlyList<PassCounter> Counters, IReadOnlyList<Diagnostic> Diagnostics)
{
    public static readonly PassResult Unchanged = new(false, [], []);
}

/// <summary>A linear stage of the conversion. Order comes from the registration list, not from a number in the name.</summary>
public interface IScorePass
{
    string Id { get; }

    PassResult Run(PipelineContext context);
}

/// <summary>
/// The pipeline's working state.
///
/// The mutation contract is explicit: a pass may mutate <see cref="Notes"/> in place and may
/// flag notes, but MUST NOT remove entries. Dropping is expressed as
/// <see cref="NoteAlteration.Dropped"/>, so nothing is ever lost between the source and the report.
/// </summary>
public sealed class PipelineContext
{
    public PipelineContext(
        SourceSong song,
        ConversionSettings settings,
        ArticulationModel model,
        IReadOnlyList<WorkNote> notes)
    {
        Song = song;
        Settings = settings;
        Model = model;
        Notes = [.. notes];
    }

    public SourceSong Song { get; }

    public ConversionSettings Settings { get; }

    public ArticulationModel Model { get; }

    public EmissionTable Emission => Model.Emission;

    public TimingProfile Timing => Model.Timing;

    public InstrumentProfile Instrument => Model.Emission.Profile;

    public List<WorkNote> Notes { get; }

    /// <summary>Chosen semitone transposition. Set by <c>fit.search</c>.</summary>
    public int Transpose { get; set; }

    /// <summary>Microseconds removed from the front by <c>time.trim</c>.</summary>
    public long TrimmedUs { get; set; }

    /// <summary>Audible notes in onset order - the projection every invariant is stated over.</summary>
    public List<WorkNote> Audible() => [.. Notes.Where(n => n.IsAudible).OrderBy(n => n.OnsetUs)];
}

/// <summary>Runs a registered list of passes and accumulates their counters and diagnostics.</summary>
public sealed class Pipeline(IReadOnlyList<IScorePass> passes)
{
    private readonly IReadOnlyList<IScorePass> _passes = passes;

    public PipelineRun Run(PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var counters = new List<PassCounter>();
        var diagnostics = new List<Diagnostic>();

        foreach (var pass in _passes)
        {
            var result = pass.Run(context);
            counters.AddRange(result.Counters);
            diagnostics.AddRange(result.Diagnostics);
        }

        return new PipelineRun(counters, diagnostics);
    }
}

public sealed record PipelineRun(IReadOnlyList<PassCounter> Counters, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Runs its inner passes until nothing changes, bounded.
///
/// Necessary because the reduction passes genuinely feed each other: smoothing a melodic spike
/// removes a note, which reopens a gap that the monophony sweep had already closed and can
/// un-strand a note that was truncated against it. Running the group once leaves notes
/// artificially staccato against silence and mis-prices the planner's edges.
/// </summary>
public sealed class FixpointGroup(string id, IReadOnlyList<IScorePass> passes, int maxIterations) : IScorePass
{
    public string Id { get; } = id;

    public PassResult Run(PipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var counters = new List<PassCounter>();
        var diagnostics = new List<Diagnostic>();
        var iterations = 0;
        bool changed;

        do
        {
            changed = false;
            iterations++;

            foreach (var pass in passes)
            {
                var result = pass.Run(context);
                changed |= result.Changed;

                // Counters accumulate across iterations; a note dropped in round two is still
                // a dropped note. Merging by id keeps the report from listing the same counter twice.
                foreach (var counter in result.Counters)
                {
                    var existing = counters.FindIndex(c => c.Id == counter.Id);
                    if (existing >= 0)
                    {
                        counters[existing] = counters[existing] with { Value = counters[existing].Value + counter.Value };
                    }
                    else
                    {
                        counters.Add(counter);
                    }
                }

                diagnostics.AddRange(result.Diagnostics);
            }
        }
        while (changed && iterations < maxIterations);

        counters.Add(new PassCounter($"{Id}.iterations", iterations));
        if (changed)
        {
            // The bound bound. Surfaced rather than swallowed: it means the group did not settle.
            counters.Add(new PassCounter($"{Id}.notConverged", 1));
        }

        return new PassResult(iterations > 1, counters, diagnostics);
    }
}
