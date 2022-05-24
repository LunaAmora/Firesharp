global using static Firesharp.Types;

namespace Firesharp;

static partial class Firesharp
{
    static List<Proc> procList = new ();
    static List<Op> program = new ();

    static void Main(string[] args)
    {
        ParseArgs(args);
        TypeCheck(program);
        GenerateWasm(program);
    }
}