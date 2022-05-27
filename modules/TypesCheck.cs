namespace Firesharp;

using DataStack = Stack<(DataType type, Loc loc)>;

static partial class Firesharp
{
    static DataStack dataStack = new ();
    static Stack<DataStack> blockStack = new ();

    static void TypeCheck(List<Op> program)
    {
        foreach (Op op in program) TypeCheckOp(op)();
    }

    static Action TypeCheckOp(Op op)  => op.Type switch
    {
        OpType.push_int  => () => dataStack.Push((DataType._int,  op.Loc)),
        OpType.push_bool => () => dataStack.Push((DataType._bool, op.Loc)),
        OpType.push_ptr  => () => dataStack.Push((DataType._ptr,  op.Loc)),
        OpType.push_str  => () => dataStack.Push((DataType._str,  op.Loc)),
        OpType.push_cstr => () => dataStack.Push((DataType._cstr, op.Loc)),
        OpType.swap => () => 
        {
            dataStack.ExpectArity(2, ArityType.any, op.Loc);
            var A = dataStack.Pop();
            var B = dataStack.Pop();
            dataStack.Push(A);
            dataStack.Push(B);
        },
        OpType.drop => () =>
        {
            dataStack.ExpectArity(1, ArityType.any, op.Loc);
            dataStack.Pop();
        },
        OpType.dup => () =>
        {
            dataStack.ExpectArity(1, ArityType.any, op.Loc);
            dataStack.Push((dataStack.Peek().type, op.Loc));
        },
        OpType.rot => () =>
        {
            dataStack.ExpectArity(3, ArityType.any, op.Loc);
            var A = dataStack.Pop();
            var B = dataStack.Pop();
            var C = dataStack.Pop();
            dataStack.Push(B);
            dataStack.Push(A);
            dataStack.Push(C);
        },
        OpType.over => () =>
        {
            dataStack.ExpectArity(2, ArityType.any, op.Loc);
            var A = dataStack.Pop();
            var B = dataStack.Pop();
            dataStack.Push(B);
            dataStack.Push(A);
            dataStack.Push(B);
        },
        OpType.if_start => () =>
        {
            dataStack.ExpectArityType(1, DataType._bool, op.Loc);
            dataStack.Pop();
            blockStack.Push(new (dataStack));
        },
        OpType._else => () =>
        {
            var oldStack = blockStack.Pop();
            blockStack.Push(new (dataStack));
            dataStack = new (oldStack);
        },
        OpType.end_if   or 
        OpType.end_else => () =>
        {
            var snapshot = blockStack.Pop().Select(element => element.type).ToList();
            var current  = dataStack.Select(element => element.type).ToList();
            var check = Enumerable.SequenceEqual(snapshot, current);

            Assert(check, op.Loc, op.Type switch
            {
                OpType.end_if   => "else-less if block is not allowed to alter the types of the arguments on the data stack",
                OpType.end_else => "both branches of the if-block must produce the same types of the arguments on the data stack",
                _ => "unreachable"
            } + $"\n[NOTE] Expected types: {ListTypes(snapshot)}\n[NOTE] Actual types:   {ListTypes(current)}");
        },
        OpType.intrinsic => () => ((IntrinsicType)op.Operand switch
        {
            IntrinsicType.plus => () =>
            {
                dataStack.ExpectArity(2, ArityType.same, op.Loc);
                dataStack.Pop();
                dataStack.Push((dataStack.Pop().type, op.Loc));
            },
            IntrinsicType.minus => () =>
            {
                dataStack.ExpectArity(2, ArityType.same, op.Loc);
                dataStack.Pop();
                dataStack.Push((dataStack.Pop().type, op.Loc));
            },
            IntrinsicType.equal => () =>
            {
                dataStack.ExpectArity(2, ArityType.same, op.Loc);
                dataStack.Pop();
                dataStack.Pop();
                dataStack.Push((DataType._bool, op.Loc));
            },
            _ => (Action) (() => Error(op.Loc, $"intrinsic value `{(IntrinsicType)op.Operand}`is not valid or is not implemented"))
        })(),
        _ => () => Error(op.Loc, $"Op type not implemented in typechecking: {op.Type}")
    };

    static void ExpectArity(this DataStack stack, int arityN, ArityType arityT, Loc loc)
    {
        Assert(stack.Count >= arityN, loc, "Stack has less elements than expected");
        Assert(arityT switch
        {
            ArityType.any  => true,
            ArityType.same => ExpectSame(stack, arityN, loc),
            _ => false
        }, loc, "Arity check failled");
    }

    static bool ExpectSame(this DataStack stack, int arityN, Loc loc)
    {
        var first = stack.ElementAt(0).type;
        return ExpectArityType(stack, arityN, first, loc);
    }

    static bool ExpectArityType(this DataStack stack, int arityN, DataType type, Loc loc)
    {
        for (int i = 0; i < arityN; i++)
        {
            var a = stack.ElementAt(i);
            if (!type.Equals(a.type)) 
            {
                Error(loc, $"expected type `{DataTypeName(type)}`, but found `{DataTypeName(a.type)}`\n{a.loc} [INFO]  the type found was declared here");
                return false;
            }
        }
        return true;
    }
}