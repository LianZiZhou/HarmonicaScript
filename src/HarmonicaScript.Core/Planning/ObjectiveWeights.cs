namespace HarmonicaScript.Core.Planning;

/// <summary>
/// The planner's cost weights, in int64 MICRO-UNITS.
///
/// No floating point anywhere in the optimiser (decision D8). That is not fastidiousness: it
/// makes the brute-force equivalence test an exact integer comparison rather than a flaky
/// epsilon one, makes tie-breaking deterministic, and makes golden files stable across
/// architectures. A Policy test asserts the absence of float opcodes in this namespace.
/// </summary>
/// <param name="Event">Cost of one physical control press or release.</param>
/// <param name="ExclusiveSwap">Extra cost when the transition swaps two mutually exclusive controls.</param>
/// <param name="SoftDeficitPerMs">Cost per millisecond of silence the plan needs but cannot have.</param>
/// <param name="DutyPerSecond">Cost of holding a control, scaled by its <c>dutyWeightMilli</c>.</param>
/// <param name="HardPenalty">Flat surcharge for an infeasible transition, so the DP routes around one rather than pricing it.</param>
public readonly record struct ObjectiveWeights(
    long Event,
    long ExclusiveSwap,
    long SoftDeficitPerMs,
    long DutyPerSecond,
    long HardPenalty)
{
    /// <summary>
    /// Shipped defaults. The MECHANICAL weights are Provenance.Invented and are pinned to the
    /// timing profile they were guessed against - a MeltySynth render cannot tell you what the
    /// game does with an insufficient gap, so no amount of listening validates them.
    /// </summary>
    public static readonly ObjectiveWeights Default = new(
        Event: 1_000_000,
        ExclusiveSwap: 1_500_000,
        SoftDeficitPerMs: 300_000,

        // Calibrated so that one second of holding 降调 (dutyWeightMilli 120) costs roughly one
        // control event. Any lower and the objective would happily park a melody in the low band
        // - where LEFT MOUSE IS FIRE - forever rather than press one extra button.
        DutyPerSecond: 8_000_000,

        HardPenalty: 1_000_000_000);
}
