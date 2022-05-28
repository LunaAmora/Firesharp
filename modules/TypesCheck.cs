namespace Firesharp;

using DataStack = Stack<(TokenType type, Loc loc)>;
using BlockStack = Stack<(Stack<(TokenType type, Loc loc)> stack, Op op)>;

static partial class Firesharp
{
    static DataStack dataStack = new ();
    static BlockStack blockStack = new (); //TODO: This method of snapshothing the datastack is really dumb, change later
    static Dictionary<Op, (int, int)> BlockContacts = new ();

    static void TypeCheck(List<Op> program)
    {
        foreach (Op op in program) TypeCheckOp(op)();
        dataStack.ExpectStackEmpty();
    }

    static Action TypeCheckOp(Op op)  => op.Type switch
    {
        OpType.push_int  => () => dataStack.Push((TokenType._int,  op.Loc)),
        OpType.push_bool => () => dataStack.Push((TokenType._bool, op.Loc)),
        OpType.push_ptr  => () => dataStack.Push((TokenType._ptr,  op.Loc)),
        OpType.push_str  => () => dataStack.Push((TokenType._str,  op.Loc)),
        OpType.push_cstr => () => dataStack.Push((TokenType._cstr, op.Loc)),
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
            dataStack.ExpectArityType(1, TokenType._bool, op.Loc);
            dataStack.Pop();
            blockStack.Push((new (dataStack), op));
        },
        OpType._else => () =>
        {
            (var oldStack, var startOp) = blockStack.Peek();
            blockStack.Push((new (dataStack), startOp));
            dataStack = new (oldStack);
        },
        OpType.end_if => () =>
        {
            var expected = blockStack.Pop().stack;
            
            ExpectStackArity(expected, dataStack, op.Loc, 
            $"Else-less if block is not allowed to alter the types of the arguments on the data stack");
        },
        OpType.end_else => () =>
        {
            var expected = blockStack.Pop().stack;

            ExpectStackArity(expected, dataStack, op.Loc, 
            $"Both branches of the if-block must produce the same types of the arguments on the data stack");

            (var oldStack, var startOp) = blockStack.Pop();

            var blockImput  = oldStack.Except(expected).Count();
            var blockOutput = expected.Except(oldStack).Count();

            BlockContacts.Add(startOp, (blockImput, blockOutput));
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
                dataStack.Push((TokenType._bool, op.Loc));
            },
            IntrinsicType.cast_bool => () =>
            {
                dataStack.ExpectArity(1, ArityType.any, op.Loc);
                dataStack.Pop();
                dataStack.Push((TokenType._bool, op.Loc));
            },
            _ => (Action) (() => Error(op.Loc, $"Intrinsic type not implemented in `TypeCheckOp` yet: `{(IntrinsicType)op.Operand}`"))
        })(),
        _ => () => Error(op.Loc, $"Op type not implemented in `TypeCheckOp` yet: {op.Type}")
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

    static bool ExpectArityType(this DataStack stack, int arityN, TokenType type, Loc loc)
    {
        for (int i = 0; i < arityN; i++)
        {
            var a = stack.ElementAt(i);
            if (!type.Equals(a.type)) 
            {
                Error(loc, $"Expected type `{TypeNames(type)}`, but found `{TypeNames(a.type)}`",
                    $"{a.loc} [INFO] The type found was declared here");
                return false;
            }
        }
        return true;
    }

    static bool ExpectStackEmpty(this DataStack stack)
    {
        return Assert(stack.Count() == 0, 
        "Expected stack to be empty at the end of the program, but found:",
        $"[INFO] Found types: {ListTypes(stack, true)}");
    }

    static bool ExpectStackArity(DataStack expected, DataStack actual, Loc loc, string errorText)
    {
        var check = Enumerable.SequenceEqual(expected.Select(a => a.type), actual.Select(a => a.type));
        return Assert(check, loc, errorText,
            $"{loc} [INFO] Expected types: {ListTypes(expected)}",
            $"{loc} [INFO] Actual types:   {ListTypes(actual)}");
    }

    static string ListTypes(this DataStack types) => dataStack.ListTypes(false);
    static string ListTypes(this DataStack types, bool verbose)
    {
        var sb = new StringBuilder("[");
        sb.AppendJoin(',',  types.Select(t => $"<{TypeNames(t.type)}>"));
        sb.Append("]");
        if (verbose)
        {
            sb.Append("\n");
            sb.AppendJoin('\n', types.Select(t => $"{t.loc} [INFO] Type `{TypeNames(t.type)}` was declared here"));
        }
        return sb.ToString();
    }
}