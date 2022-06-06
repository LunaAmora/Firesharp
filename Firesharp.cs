global using static Firesharp.Firesharp;
global using static Firesharp.Types;
global using System.Text;
using CliFx;

namespace Firesharp;

static partial class Firesharp
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
