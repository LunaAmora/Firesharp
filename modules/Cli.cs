using System.Runtime.CompilerServices;
using System.Diagnostics;
using CliFx.Infrastructure;
using CliWrap.Exceptions;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliWrap;
using CliFx;

namespace Firesharp.Cli;

[Command("-com")]
public class CompileCommand : ICommand
{
    [CommandParameter(0, Description = "Compile a `.fire` file to WebAssembly.")]
    public FileInfo? InputFile { get; init; }
    [CommandOption("run", 'r', Description = "Run the `.wasm` file with a runtime.")]
    public bool Run { get; init; }
    [CommandOption("debug", 'd', Description = "Add OpTypes information to output `.wat` file.")]
    public bool DebugMode { get; init; }
    [CommandOption("wat", 'w', Description = "Decompile the `.wasm` file into the `.wat`.")]
    public bool Wat { get; init; }
    [CommandOption("silent", 's', Description = "Don't print any info about compilation phases.")]
    public bool SilentMode { get; init; }
    [CommandOption("graph", 'g', Description = "Generate a `.svg` call graph of the compiled program. (Needs Graphviz)")]
    public bool Graph { get; init; }
    [CommandOption("opt", 'p', Description = "Optimize the `.wasm` file to reduce it's size. (Needs Binaryen)")]
    public bool Opt { get; init; }
    [CommandOption("runtime", 't', Description = "Sets the runtime to be used by the `-r` option.")]
    public string Runtime { get; init; } = "wasmtime";

    public async ValueTask ExecuteAsync(IConsole console)
    {
        FConsole = console;
        debug = DebugMode;
        silent = SilentMode;
        if(InputFile is {})
        {
            Assert(InputFile.Extension.Equals(".fire"), error: "The input file name provided is not valid");
            Assert(InputFile.Exists, error: "Failed to find the provided file");

            var watch = StartWatch();

            TryReadFile(InputFile);
            Parser.ParseTokens();
            watch.TimeIt($"Compilation");

            TypeChecker.TypeCheck(Parser.program);
            watch.TimeIt($"Typechecking");

            if(Path.GetDirectoryName(InputFile.ToString()) is not string dir)
            {
                Error(error: "Could not resolve file directory");
                return;
            }

            var buildPath = Path.Combine(dir, "build");
            if(!Directory.Exists(buildPath))
                Directory.CreateDirectory(buildPath);
            var outPath = Path.Combine(buildPath, "out.wat");
            var outWasm = Path.Combine(buildPath, "out.wasm");

            Generator.GenerateWasm(Parser.program, outPath);
            watch.TimeIt($"Generation", false);
            
            if(Graph)
            {
                var outGraph = Path.Combine(buildPath, "out.svg");

                await CmdEcho("wat2wasm", "--debug-names", outPath, "-o", outWasm);
                await CmdEcho("wasm-opt", new []{"-q", "--print-call-graph", "--enable-multivalue", outWasm},
                              "dot", "-Tsvg", "-q", "-o", outGraph);
            }
            else    await CmdEcho("wat2wasm", outPath, "-o", outWasm);
            if(Opt) await CmdEcho("wasm-opt", "-O4", "--enable-multivalue", outWasm, "-o", outWasm);
            if(Wat) await CmdEcho("wasm2wat", outWasm, "-o", outPath);
            if(Run) await CmdEcho(Runtime, outWasm);
        }
    }
}

static class Cli
{
    public static bool debug = false;
    public static bool silent = false;
    static IConsole? _console;

    public static void TryReadRelative(Loc loc, string target)
    {
        Assert(target.EndsWith(".fire"), loc, $"The target include file is not valid: {target}");
        if(Path.GetDirectoryName(loc.file) is {} dir &&
           new FileInfo(Path.Combine(dir, target)) is {} tPath &&
           tPath.Exists)
        {
            TryReadFile(tPath, false);
        }
        else Error(loc, $"Failed to find the target include file: {target}");
    }

    public static void TryReadFile(FileInfo fileInfo, bool first = true)
    {
        var filePath = fileInfo.ToString();
        Info(text: first ? "Compiling {0}" : "Including {0}", arg: filePath);
        try
        {
            using (var file = fileInfo.Open(FileMode.Open))
            {
                Parser.TokenizeFile(file, filePath);
            }
        }
        catch (System.IO.FileNotFoundException)
        {
            Error(error: $"Failed to open the file: {filePath}");
        }
    }

    public static IConsole FConsole
    {
        get
        {
            Debug.Assert(_console is {});
            return _console;
        }
        set => _console = value;
    }

    public static async Task CmdEcho(string target, string[] arg, string pipe, params string[] arg2)
    {
        try
        {
            var cmd = CliWrap.Cli.Wrap(target)
                .WithArguments(arg) |
                (WriteLine, ErrorWriteLine) |
                CliWrap.Cli.Wrap(pipe)
                .WithArguments(arg2) |
                (WriteLine, ErrorWriteLine);
            WritePrefix("[CMD] ", $"{target} {String.Join(" ", arg)}");
            WritePrefix("[CMD] ", cmd.ToString());
            await cmd.ExecuteAsync();
        }
        catch (Exception)
        {
            Error(error: "External command error, please report this in the project's github!");
        }
    }

    public static async Task CmdEcho(string target, params string[] arg)
    {
        try
        {
            var cmd = CliWrap.Cli.Wrap(target)
                .WithArguments(arg) |
                (WriteLine, ErrorWriteLine);
            WritePrefix("[CMD] ", cmd.ToString());
            await cmd.ExecuteAsync();
        }
        catch (Exception)
        {
            Error(error: "External command error, please report this in the project's github!");
        }
    }

    public static void ErrorWriteLine(string error)
    {
        FConsole.ForegroundColor = ConsoleColor.Red;
        FConsole.Error.WriteLine(error);
    }

    public static void WriteLine(string text)
        => FConsole.Output.WriteLine(text);

    public static void Write(string format, params object?[] arg)
        => FConsole.Output.Write(format, arg);
    
    public static void WriteLine(string format, params object?[] arg)
        => FConsole.Output.WriteLine(format, arg);
    
    public static void WritePrefix(string prefix, string format, params object?[] arg)
    {
        if(silent) return;
        Write(prefix);
        WriteLine(format, arg);
    }

    public static void WritePrefix(string prefix, ConsoleColor color, string format, params object?[] arg)
    {
        if(silent) return;
        FConsole.ForegroundColor = color;
        WritePrefix(prefix, format, arg);
        FConsole.ResetColor();
    }
    
    public static void Warn(Loc loc = default, string text = "", params object?[] arg)
        => WritePrefix($"{loc}[WARN] ", ConsoleColor.Yellow, text, arg);
    
    public static void Info(Loc loc = default, string text = "", params object?[] arg)
        => WritePrefix($"{loc}[INFO] ", text, arg);

    public static void Here(string message,
        [CallerFilePath] string path = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        var loc = new Loc(Path.GetRelativePath(Directory.GetCurrentDirectory(), path), lineNumber, 0);
        WritePrefix($"./{loc}[Compiler] ", ConsoleColor.Yellow, message);
    }

    public static string ErrorHere(
        string hereText = "",
        Loc loc = default,
        [CallerFilePath] string path = "",  
        [CallerLineNumber] int lineNumber = 0,
        params string[] error)
    {
        Here(hereText, path, lineNumber);
        var result = string.Empty;
        if(error.Count() > 0) 
            return Error(loc, error);
        else if(!string.IsNullOrEmpty(loc.file))
            return Error(loc, "Error found here");
        else Environment.Exit(1);
        return string.Empty;
    }

    public static string Error(Loc loc = default, params string[] error) 
        => Error(1, $"{loc}[ERROR] {string.Join($"\n", error)}");

    public static string Error(int exitCode, string error)
        => throw new CommandException(error, exitCode);

    public static bool Assert(bool cond, Loc loc = default, params string[] error)
    {
        if(!cond) Error(loc, error);
        return cond;
    }

    public static Stopwatch StartWatch()
    {
        var watch = new System.Diagnostics.Stopwatch();
        watch.Start();
        return watch;
    }

    public static void TimeIt(this Stopwatch watch, string timeInfo, bool restart = true)
    {
        watch.Stop();
        Info(text: $"{timeInfo} took: {watch.ElapsedMilliseconds} ms");
        if(restart) watch.Restart();
    }
}
