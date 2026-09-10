using System.CommandLine;

namespace HarmonicaScript.Cli;

internal static class Program
{
    internal const string Version = "0.9.0-dev";

    internal static int Main(string[] args)
    {
        var root = new RootCommand("HarmonicaScript - MIDI to Delta Force harmonica converter");

        root.Add(Commands.Convert());
        root.Add(Commands.Export());
        root.Add(Commands.Targets());
        root.Add(Commands.Audition());
        root.Add(Commands.Profile());
        root.Add(Commands.Simulate());
        root.Add(Commands.Fixtures());

        return root.Parse(args).Invoke();
    }
}
