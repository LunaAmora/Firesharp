using System.Runtime.CompilerServices;
using System.Diagnostics;
using CliFx.Infrastructure;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliWrap;
using CliFx;

namespace Firesharp;

[Command("-com")]
public class CompileCommand : ICommand
{
    [CommandParameter(0, Description = "Compile a `.fire` file to WebAssembly.")]
    public FileInfo? InputFile { get; init; }
    [CommandOption("debug", 'd', Description = "Add OpTypes information to output `.wat` file")]
    public bool DebugMode { get; init; } = false;

    public async ValueTask ExecuteAsync(IConsole console)
    {
        FConsole = console;
        debug = DebugMode;
        if(InputFile is {})
        {
            Assert(InputFile.Extension.Equals(".fire"), error: "The input file name provided is not valid");
            Assert(InputFile.Exists, error: "Failed to find the provided file");
            TryReadFile(InputFile);
            Parser.ParseTokens();
            TypeChecker.TypeCheck(Parser.program);
            await Generator.GenerateWasm(Parser.program, InputFile.ToString());
        }
    }
}

static partial class Firesharp
{
    public static bool debug = false;
    static IConsole? _console;

    public static void TryReadRelative(Loc loc, string target)
    {
        Assert(target.EndsWith(".fire"), loc, $"The target include file is not valid: {target}");
        var current = loc.file;
        if(Path.GetDirectoryName(current) is {} dir &&
           new FileInfo(Path.Combine(dir, target)) is {} tPath &&
           tPath.Exists)
        {
            TryReadFile(tPath);
        }
        else Error(loc, $"Failed to find the target include file: {target}");
    }

    public static void TryReadFile(FileInfo fileInfo)
    {
        var filePath = fileInfo.ToString();
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

    public static async Task CmdEcho(string target, params string[] arg)
    {
        var cmd = Cli.Wrap(target)
            .WithValidation(CommandResultValidation.None)
            .WithArguments(arg) |
            (FConsole.Output.WriteLine, FConsole.Error.WriteLine);
        WritePrefix("[CMD] ", cmd.ToString());
        var result = await cmd.ExecuteAsync();
        Assert(result.ExitCode == 0, error: "External command error, please report this in the project's github!");
    }

    public static void Write(string format, params object?[] arg)
        => FConsole.Output.Write(format, arg);
    
    public static void WriteLine(string format, params object?[] arg)
        => FConsole.Output.WriteLine(format, arg);
    
    public static void WritePrefix(string prefix, string format, params object?[] arg)
    {
        Write(prefix);
        WriteLine(format, arg);
    }

    public static void WritePrefix(string prefix, ConsoleColor color, string format, params object?[] arg)
    {
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
}
