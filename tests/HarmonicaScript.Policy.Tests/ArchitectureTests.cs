using Mono.Cecil;
using Mono.Cecil.Cil;

namespace HarmonicaScript.Policy.Tests;

/// <summary>
/// The architectural bans, as EXECUTABLE ASSERTIONS rather than prose in a table.
///
/// "Core must not touch I/O" and "the app never opens a socket" are the kind of rule that decays
/// the moment someone is in a hurry, and a README cannot fail a build. These read the compiled IL
/// with Cecil, so they hold regardless of intent, refactoring or a convenient using directive.
/// </summary>
public sealed class ArchitectureTests
{
    /// <summary>The CLI's assembly is named for its command (hsc), not for its project.</summary>
    private static string FileNameOf(string projectName) =>
        projectName == "HarmonicaScript.Cli" ? "hsc" : projectName;

    private static ModuleDefinition Module(string assemblyName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileNameOf(assemblyName) + ".dll");
        Assert.True(File.Exists(path), $"{assemblyName}.dll was not copied next to the tests");
        return ModuleDefinition.ReadModule(path);
    }

    private static IEnumerable<(MethodDefinition Method, Instruction Instruction)> Instructions(ModuleDefinition module)
    {
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    yield return (method, instruction);
                }
            }
        }
    }

    private static IEnumerable<(MethodDefinition Method, string Target)> Calls(ModuleDefinition module) =>
        Instructions(module)
            .Where(x => x.Instruction.Operand is MethodReference)
            .Select(x => (x.Method, ((MethodReference)x.Instruction.Operand).DeclaringType.FullName));

    [Theory]
    [InlineData("HarmonicaScript.Core")]
    [InlineData("HarmonicaScript.Profiles")]
    [InlineData("HarmonicaScript.Midi")]
    [InlineData("HarmonicaScript.Export")]
    [InlineData("HarmonicaScript.Playback")]
    [InlineData("HarmonicaScript.Playback.Windows")]
    [InlineData("HarmonicaScript.Playback.MacOs")]
    [InlineData("HarmonicaScript.Project")]
    [InlineData("HarmonicaScript.Audition")]
    [InlineData("HarmonicaScript.Cli")]
    [InlineData("HarmonicaScript.App")]
    public void NoAssemblyEverOpensASocket(string assemblyName)
    {
        // Decision D12: the application makes no outbound network connection, ever. This is a
        // first-party invariant precisely SO a user can corroborate it with a firewall in ten
        // seconds - which only means anything if it is enforced rather than promised.
        using var module = Module(assemblyName);

        var offenders = Calls(module)
            .Where(c => c.Target.StartsWith("System.Net.", StringComparison.Ordinal))
            .Select(c => $"{c.Method.FullName} -> {c.Target}")
            .Distinct()
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CoreIsPureComputationWithNoFileOrStreamAccess()
    {
        using var module = Module("HarmonicaScript.Core");

        var offenders = Calls(module)
            .Where(c => c.Target is "System.IO.File" or "System.IO.Directory" or "System.IO.Path"
                or "System.IO.FileStream" or "System.IO.StreamReader" or "System.IO.StreamWriter")
            .Select(c => $"{c.Method.FullName} -> {c.Target}")
            .Distinct()
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void MidiNeverTouchesDryWetMidisMultimediaLayer()
    {
        // Decision D2. The Multimedia namespace is the device/playback layer that the stripped
        // native blobs serve; our scheduler and IInputBackend replace it deliberately.
        using var module = Module("HarmonicaScript.Midi");

        var offenders = module.GetTypeReferences()
            .Where(t => t.FullName.StartsWith("Melanchall.DryWetMidi.Multimedia", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .Distinct()
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void OnlyTheMidiAssemblyReferencesDryWetMidi()
    {
        foreach (var name in new[] { "HarmonicaScript.Core", "HarmonicaScript.Profiles", "HarmonicaScript.Playback", "HarmonicaScript.Audition" })
        {
            using var module = Module(name);
            Assert.DoesNotContain(module.AssemblyReferences, r => r.Name.Contains("DryWetMidi", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheOptimiserContainsNoFloatingPointArithmetic()
    {
        // Decision D8. Integer-only is what makes the brute-force equivalence test an EXACT
        // comparison rather than a flaky epsilon one, makes tie-breaking deterministic, and makes
        // golden files stable across architectures. A stray double would quietly undo all three.
        using var module = Module("HarmonicaScript.Core");

        var floatOpcodes = new HashSet<Code>
        {
            Code.Add_Ovf_Un, // placeholder to keep the set non-empty for clarity below
        };

        floatOpcodes.Clear();
        floatOpcodes.UnionWith([Code.Conv_R4, Code.Conv_R8, Code.Conv_R_Un, Code.Ldc_R4, Code.Ldc_R8]);

        var optimiserTypes = new[]
        {
            "HarmonicaScript.Core.Planning.ModifierPlanner",
            "HarmonicaScript.Core.Planning.TranspositionSearch",
            "HarmonicaScript.Core.Planning.ObjectiveWeights",
        };

        var offenders = new List<string>();
        foreach (var type in module.GetTypes().Where(t => optimiserTypes.Contains(t.FullName)))
        {
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions.Where(i => floatOpcodes.Contains(i.OpCode.Code)))
                {
                    offenders.Add($"{method.FullName}: {instruction.OpCode}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoAssemblyDependsOnSharpHook()
    {
        // SharpHook's bundled native libuiohook ships GPL-3.0/LGPL-3.0 and would attach
        // relinking obligations to an MIT binary. It is the one licence trap in the graph.
        foreach (var name in new[]
        {
            "HarmonicaScript.Core", "HarmonicaScript.Playback", "HarmonicaScript.Playback.Windows",
            "HarmonicaScript.Playback.MacOs", "HarmonicaScript.Cli", "HarmonicaScript.App",
        })
        {
            using var module = Module(name);
            Assert.DoesNotContain(module.AssemblyReferences, r => r.Name.Contains("SharpHook", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void NoAssemblyDependsOnAPaidAvaloniaComponent()
    {
        using var module = Module("HarmonicaScript.App");

        Assert.DoesNotContain(module.AssemblyReferences, r => r.Name.Contains("TreeDataGrid", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(module.AssemblyReferences, r => r.Name.Contains("AvaloniaUI.Licensing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheGuiContainsNoConversionAlgorithm()
    {
        // The GUI must call the same ConversionService the CLI does, so a bug fixed for one is
        // fixed for both and a user's CLI reproduction matches what the GUI did.
        using var module = Module("HarmonicaScript.App");

        var offenders = Calls(module)
            .Where(c => c.Target is "HarmonicaScript.Core.Planning.ModifierPlanner"
                or "HarmonicaScript.Core.Planning.TranspositionSearch"
                or "HarmonicaScript.Core.Conversion.Converter")
            .Select(c => c.Method.FullName)
            .Distinct()
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoAssemblyUsesLowLevelKeyboardHooks()
    {
        // SetWindowsHookEx(WH_KEYBOARD_LL) is the API anti-cheats watch most closely, and we
        // have no need for it: RegisterHotKey covers every hotkey this app wants.
        foreach (var name in new[] { "HarmonicaScript.Playback", "HarmonicaScript.Playback.Windows", "HarmonicaScript.App", "HarmonicaScript.Cli" })
        {
            using var module = Module(name);
            var offenders = module.GetTypes()
                .SelectMany(t => t.Methods)
                .Where(m => m.Name.Contains("SetWindowsHookEx", StringComparison.Ordinal))
                .ToList();

            Assert.Empty(offenders);
        }
    }

    [Fact]
    public void NoAssemblyReadsAnotherProcessSMemoryOrOpensTheGameProcess()
    {
        // The engineering charter, enforced. OpenProcess on the game is the one cross-process
        // behaviour ACE advertises intercepting, and the information gain never justified it.
        foreach (var name in new[] { "HarmonicaScript.Playback.Windows", "HarmonicaScript.Playback", "HarmonicaScript.App", "HarmonicaScript.Cli" })
        {
            using var module = Module(name);
            var offenders = module.GetTypes()
                .SelectMany(t => t.Methods)
                .Where(m => m.Name is "OpenProcess" or "ReadProcessMemory" or "WriteProcessMemory" or "VirtualAllocEx")
                .Select(m => m.FullName)
                .ToList();

            Assert.Empty(offenders);
        }
    }
}
