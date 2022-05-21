namespace Firesharp;

static partial class Firesharp
{
    static List<Proc> procList = new List<Proc>();
    static List<Op> program = new List<Op>();

    static void Main(string[] args)
    {
        ParseArgs(args);
        TypeCheck(program);
        GenerateWasm(program);
    }
}