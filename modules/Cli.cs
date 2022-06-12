using System.Runtime.CompilerServices;
using System.Diagnostics;
using CliFx.Infrastructure;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx;

namespace Firesharp;

[Command("-com")]
public class CompileCommand : ICommand
{
    [CommandParameter(0, Description = "Compile a `.fire` file to WebAssembly.")]
    public FileInfo? File { get; init; }

    public async ValueTask ExecuteAsync(IConsole console)
    {
        FConsole = console;
        if(File is {})
        {
            Assert(File.Extension.Equals(".fire"), error: "The input file name provided is not valid");
            Filepath = File.ToString();
            try
            {
                using (var file = File.Open(FileMode.Open))
                {
                    var program = Parser.ParseFile(file, Filepath);
                    TypeChecker.TypeCheck(program);
                    await Generator.GenerateWasm(program);
                }
            }
            catch(System.IO.FileNotFoundException)
            {
                Error(error: $"File not found `{Filepath}`");
            }
        }
    }
}

static partial class Firesharp
{
    static IConsole? _console;
    static string? _filepath;

    public static IConsole FConsole
    {
        get
        {
            Debug.Assert(_console is {});
            return _console;
        }
        set => _console = value;
    }

    public static string Filepath
    {
        get
        {
            Debug.Assert(_filepath is {});
            return _filepath;
        }
        set => _filepath = value;
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
        if(error.Count() > 0) return Error(loc, error);
        else return Error(loc, "Error found here");
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
