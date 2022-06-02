namespace Firesharp;

static partial class Firesharp
{
    static string? filepath;

    static void ParseArgs(string[] args)
    {
        Assert(args.Count() != 0, "subcommand is not provided");

        int i = 0;
        while (i < args.Count())
        {
            switch (args[i++])
            {
                case "-com":
                {
                    Assert(args.Count() > i, "no input file is provided for the compilation");
                    filepath = args[i++];
                    Assert(filepath.Contains(".fire"), "the input file name provided is not valid");
                    
                    try
                    {
                        using (var file = new FileStream(filepath, FileMode.Open))
                        {
                            ParseFile(file, filepath);
                        }
                    }
                    catch(System.IO.FileNotFoundException)
                    {
                        Error($"File not found `{args[i-1]}`");
                    }
                    return;
                }
                default: Error($"Unknown subcommand `{args[i-1]}`"); return;
            }
        }
    }

    static void Write(string format, params object?[]? arg) => Console.WriteLine(format, arg);
    static void Info(string format, params object?[]? arg)
    {
        Console.Write("[INFO] ");
        Write(format, arg);
    }
    
    static void Info(Loc loc, string format, params object?[]? arg)
    {
        Console.Write($"{loc} [INFO] ");
        Write(format, arg);
    }

    static void Exit() => Environment.Exit(0);

    static string Error(int exitCode, params string[] errorText)
    {
        if(errorText is string[] errors)
        {
            foreach(var error in errors)
            {
                Console.Error.WriteLine(error);
            }
        }
        
        Environment.Exit(exitCode);
        return string.Empty;
    }

    static string Error(params string[] errorText) => Error(-1, $"[ERROR] {string.Join("\n", errorText)}");
    static string Error(Loc loc, params string[] errorText) => Error(-1, $"{loc} [ERROR] {string.Join($"\n", errorText)}");

    static bool Assert(bool cond, params string[] errorText)
    {
        if(!cond) Error(errorText);
        return cond;
    }

    static bool Assert(bool cond, Loc loc, params string[] errorText)
    {
        if(!cond) Error(loc, errorText);
        return cond;
    }
}
