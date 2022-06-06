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
            Assert(File.Extension.Equals(".fire"), "The input file name provided is not valid");
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
                Error($"File not found `{Filepath}`");
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
    
    public static void Warn(string format, params object?[] arg)
        => WritePrefix("[WARN] ", ConsoleColor.Yellow, format, arg);
    
    public static void Warn(Loc loc, string format, params object?[] arg)
        => WritePrefix($"{loc} [WARN] ", ConsoleColor.Yellow, format, arg);

    public static void Info(string format, params object?[] arg)
        => WritePrefix("[INFO] ", format, arg);
    
    public static void Info(Loc loc, string format, params object?[] arg)
        => WritePrefix($"{loc} [INFO] ", format, arg);

    public static string Error(params string[] errorText) 
        => Error(1, $"[ERROR] {string.Join("\n", errorText)}");
    
    public static string Error(Loc loc, params string[] errorText) 
        => Error(1, $"{loc} [ERROR] {string.Join($"\n", errorText)}");

    public static string Error(int exitCode, string errorText)
        => throw new CommandException(errorText, exitCode);

    public static bool Assert(bool cond, params string[] errorText)
    {
        if(!cond) Error(errorText);
        return cond;
    }

    public static bool Assert(bool cond, Loc loc, params string[] errorText)
    {
        if(!cond) Error(loc, errorText);
        return cond;
    }
}
