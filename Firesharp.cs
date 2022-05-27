global using static Firesharp.Firesharp;

namespace Firesharp;

static partial class Firesharp
{
    static List<Op> program = new ();

    static void Main(string[] args)
    {
        ParseArgs(args);
        TypeCheck(program);
        GenerateWasm(program);
    }
}