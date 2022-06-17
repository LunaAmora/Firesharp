global using static Firesharp.Cli.Cli;
global using Firesharp.Types;
global using System.Text;
using CliFx;

namespace Firesharp;

public class Firesharp
{
    public static async Task<int> Main() =>
        await new CliApplicationBuilder()
            .SetDescription("Compiler for the Firesharp language.")
            .SetExecutableName("Firesharp")
            .SetVersion("v0.1.0")
            .AllowDebugMode(false)
            .AllowPreviewMode(false)
            .AddCommandsFromThisAssembly()
            .Build()
            .RunAsync();
}
