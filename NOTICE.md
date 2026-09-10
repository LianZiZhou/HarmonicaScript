# Third-party notices

HarmonicaScript is MIT licensed. It depends on, and derives ideas from, the following.

## Dependencies (all permissive)

| Package | Licence | Used for |
|---|---|---|
| Melanchall.DryWetMidi 8.0.3 | MIT | SMF parsing only (see `docs/PROVENANCE.md` for why the Multimedia layer is banned) |
| Avalonia 11.3.21 and satellites | MIT | Desktop UI |
| Avalonia.Controls.DataGrid 11.3.13 | MIT | Track table and note editor grid |
| CommunityToolkit.Mvvm 8.4.2 | MIT | Source-generated MVVM |
| MeltySynth 2.4.1 | MIT | Offline SoundFont rendering for the audition WAV |
| System.CommandLine 2.0.12 | MIT | `hsc` command line |
| Microsoft.Windows.CsWin32 0.3.333 | MIT | Generated P/Invoke signatures (build-time only) |
| xunit.v3, Verify.XunitV3, CsCheck, Mono.Cecil | Apache-2.0 / MIT | Tests only, never shipped |

**Deliberately not referenced:** `SharpHook` (its bundled native libuiohook is GPL-3.0/LGPL-3.0
and would attach relinking obligations to an MIT binary) and `Avalonia.Controls.TreeDataGrid`
(a paid component with a hard dependency on `AvaloniaUI.Licensing`).

## Ideas, credited

`sabihoshi/GenshinLyreMidiPlayer` is MIT and is the one prior-art codebase we may borrow
expression from. Anything taken from it is marked at the call site and listed here.

Everything else in `docs/PROVENANCE.md` is a clean-room derivation from public documentation
or observable behaviour, not copied expression.
