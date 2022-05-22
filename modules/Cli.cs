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
                        using (FileStream file = new FileStream(filepath, FileMode.Open))
                        {
                            ParseFile(file);
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

    static void Error(string errorText) => Error(errorText, -1);
    static void Exit() => Environment.Exit(0);

    static void Error(string errorText, int exitCode)
    {
        Console.Error.WriteLine($"[ERROR] {errorText}");
        Environment.Exit(exitCode);
    }

    static bool Assert(bool cond, string errorText)
    {
        if(!cond)
        {
            Error(errorText);
        }
        return cond;
    }
}