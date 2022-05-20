namespace Firesharp;

static partial class Firesharp
{
    static Stack<DataType> dataStack = new Stack<DataType>();
    
    static List<Proc> procList = new List<Proc>();
    static Queue<Op>  program  = new Queue<Op>();

    static List<string> stringList = new List<string>();

    static StreamWriter output = StreamWriter.Null;

    static void Main(string[] args)
    {
        program.Enqueue(Op.New(ActionType.push_int, 69));
        program.Enqueue(Op.New(ActionType.push_int, 420));
        program.Enqueue(Op.New(IntrinsicType.equal));
        program.Enqueue(Op.New(ActionType.drop));

        TypeCheck(new Queue<Op>(program));
        GenerateWasm(program);
    }
}