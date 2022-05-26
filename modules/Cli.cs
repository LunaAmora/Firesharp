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
                        Error($"file not found `{args[i-1]}`");
                    }
                    return;
                }
                default: Error($"unknown subcommand `{args[i-1]}`"); return;
            }
        }
    }

    static void Exit() => Environment.Exit(0);

    static string Error(string errorText, int exitCode)
    {
        Console.Error.WriteLine(errorText);
        Environment.Exit(exitCode);
        return string.Empty;
    }

    static string Error(string errorText) => Error($"[ERROR] {errorText}", -1);
    static string Error(Loc loc, string errorText) => Error($"{loc} [ERROR] {errorText}", -1);

    static bool Assert(bool cond, string errorText)
    {
        if(!cond) Error(errorText);
        return cond;
    }

    static bool Assert(bool cond, Loc loc, string errorText)
    {
        if(!cond) Error(loc, errorText);
        return cond;
    }

    static string Assert(bool cond, string successText, string errorText)
    {
        if(!cond) return Error(errorText);
        else return successText;
    }

    static string Assert(bool cond, Loc loc, string successText, string errorText)
    {
        if(!cond) return Error(loc, errorText);
        else return successText;
    }
}