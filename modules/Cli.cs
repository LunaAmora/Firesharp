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
                        Exit($"file not found `{args[i-1]}`");
                    }
                    return;
                }
                default: Exit($"unknown subcommand `{args[i-1]}`"); return;
            }
        }
    }

    static void Exit(string errorText, int exitCode)
    {
        Console.Error.WriteLine($"[ERROR] {errorText}");
        Environment.Exit(exitCode);
    }

    static void Exit(string errorText) => Exit(errorText, -1);

    static void Assert(bool cond, string errorText)
    {
        if(!cond)
        {
            Exit(errorText);
        }
    }
}