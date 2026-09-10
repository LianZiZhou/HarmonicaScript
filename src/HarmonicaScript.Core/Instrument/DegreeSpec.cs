namespace HarmonicaScript.Core.Instrument;

/// <summary>
/// One playable note key. For the Delta Force harmonica these are Z X C V B N M ',' carrying
/// jianpu 1-7 plus the octave-up 1, i.e. semitone offsets 0 2 4 5 7 9 11 12 from the base pitch.
/// </summary>
/// <param name="Index">1-based position in the row, as printed in the game's UI.</param>
/// <param name="Semitone">Semitones above the profile's base pitch when no modifier is active.</param>
/// <param name="Jianpu">Numbered-notation label, e.g. "1" or "1^" for the octave-up do.</param>
/// <param name="Binding">The physical key.</param>
public sealed record DegreeSpec(int Index, int Semitone, string Jianpu, Binding Binding);
