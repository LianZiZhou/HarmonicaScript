namespace HarmonicaScript.Core.Instrument;

/// <summary>
/// A legal set of simultaneously active modifiers, derived at profile load from
/// <see cref="InstrumentProfile.Modifiers"/>, their exclusion groups and
/// <see cref="InstrumentProfile.MaxSimultaneousModifiers"/>. Never a hand-written enum:
/// the planner loops <c>for st in 0..StateCount-1</c> and knows nothing about octaves.
/// For the Delta Force harmonica this yields exactly 6 states.
/// </summary>
/// <param name="Index">Dense index into <see cref="EmissionTable.States"/>.</param>
/// <param name="Mask">Bit <c>i</c> set means <c>Modifiers[i]</c> is active.</param>
/// <param name="SemitoneDelta">Sum of the active modifiers' deltas.</param>
public readonly record struct ModState(byte Index, ushort Mask, int SemitoneDelta)
{
    public bool Has(int modifierIndex) => (Mask & (1 << modifierIndex)) != 0;

    public int Count => System.Numerics.BitOperations.PopCount(Mask);

    public bool IsNeutral => Mask == 0;
}
