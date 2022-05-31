namespace Firesharp;

using DataStack = Stack<(TokenType type, Loc loc)>;

static partial class Firesharp
{
    static DataStack dataStack = new ();
    static Stack<(DataStack stack, Op op)> blockStack = new (); //TODO: This method of snapshotting the datastack is really dumb, change later
    static Dictionary<Op, (int, int)> blockContacts = new ();

    static void TypeCheck(List<Op> program)
    {
        foreach (Op op in program) TypeCheckOp(op)();
    }

    static Action TypeCheckOp(Op op)  => op.type switch
    {
        OpType.push_int  => () => dataStack.Push((TokenType._int,  op.loc)),
        OpType.push_bool => () => dataStack.Push((TokenType._bool, op.loc)),
        OpType.push_ptr  => () => dataStack.Push((TokenType._ptr,  op.loc)),
        OpType.push_str  => () => 
        {
            dataStack.Push((TokenType._int,  op.loc));
            dataStack.Push((TokenType._ptr,  op.loc));
        },
        OpType.push_cstr => () => dataStack.Push((TokenType._ptr, op.loc)),
        OpType.swap => () => 
        {
            dataStack.ExpectArity(2, ArityType.any, op.loc);
            var A = dataStack.Pop();
            var B = dataStack.Pop();
            dataStack.Push(A);
            dataStack.Push(B);
        },
        OpType.drop => () =>
        {
            dataStack.ExpectArity(1, ArityType.any, op.loc);
            dataStack.Pop();
        },
        OpType.dup => () =>
        {
            dataStack.ExpectArity(1, ArityType.any, op.loc);
            dataStack.Push((dataStack.Peek().type, op.loc));
        },
        OpType.rot => () =>
        {
            dataStack.ExpectArity(3, ArityType.any, op.loc);
            var A = dataStack.Pop();
            var B = dataStack.Pop();
            var C = dataStack.Pop();
            dataStack.Push(B);
            dataStack.Push(A);
            dataStack.Push(C);
        },
        OpType.over => () =>
        {
            dataStack.ExpectArity(2, ArityType.any, op.loc);
            var A = dataStack.Pop();
            var B = dataStack.Pop();
            dataStack.Push(B);
            dataStack.Push(A);
            dataStack.Push(B);
        },
        OpType.push_global_mem => () => dataStack.Push((TokenType._ptr, op.loc)),
        OpType.push_local_mem => () => dataStack.Push((TokenType._ptr, op.loc)),
        OpType.call => () => 
        {
            var proc = procList[op.operand].contract;
            var ins = new List<TokenType>(proc.ins);
            ins.Reverse();
            dataStack.ExpectArity(op.loc, ins.ToArray());
            ins.ForEach(_ => dataStack.Pop());
            var outs = proc.outs;
            for (int i = 0; i < outs.Count; i++) dataStack.Push((outs[i], op.loc));
        },
        OpType.prep_proc => () =>
        {
            Assert(!insideProc, op.loc, "Cannot define a procedure inside of another procedure");
            currentProc = procList[op.operand];
            currentProc.contract.ins.ForEach(type => dataStack.Push((type, op.loc)));
        },
        OpType.end_proc => () =>
        {
            Assert(insideProc, "Unreachable");
            if(currentProc is Proc proc)
            {
                var outs = proc.contract.outs;
                outs.Reverse();
                dataStack.ExpectStackExact(op.loc, outs.ToArray());
                outs.ForEach(_ => dataStack.Pop());
            }
            currentProc = null;
            dataStack = new();
        },
        OpType.if_start => () =>
        {
            dataStack.ExpectArity(1, TokenType._bool, op.loc);
            dataStack.Pop();
            blockStack.Push((dataStack.Clone(), op));
        },
        OpType._else => () =>
        {
            (var oldStack, var startOp) = blockStack.Peek();
            blockStack.Push((dataStack.Clone(), startOp));
            dataStack = oldStack.Clone();
        },
        OpType.end_if => () =>
        {
            var expected = blockStack.Pop().stack;
            
            ExpectStackArity(expected, dataStack, op.loc, 
            $"Else-less if block is not allowed to alter the types of the arguments on the data stack");
        },
        OpType.end_else => () =>
        {
            var expected = blockStack.Pop().stack;

            ExpectStackArity(expected, dataStack, op.loc, 
            $"Both branches of the if-block must produce the same types of the arguments on the data stack");

            (var oldStack, var startOp) = blockStack.Pop();

            var blockInput  = oldStack.Except(expected).Count();
            var blockOutput = expected.Except(oldStack).Count();

            blockContacts.Add(startOp, (blockInput, blockOutput));
        },
        OpType.intrinsic => () => ((IntrinsicType)op.operand switch
        {
            IntrinsicType.plus => () =>
            {
                dataStack.ExpectArity(2, ArityType.same, op.loc);
                dataStack.Pop();
                dataStack.Push((dataStack.Pop().type, op.loc));
            },
            IntrinsicType.minus => () =>
            {
                dataStack.ExpectArity(2, ArityType.same, op.loc);
                dataStack.Pop();
                dataStack.Push((dataStack.Pop().type, op.loc));
            },
            IntrinsicType.equal => () =>
            {
                dataStack.ExpectArity(2, ArityType.same, op.loc);
                dataStack.Pop();
                dataStack.Pop();
                dataStack.Push((TokenType._bool, op.loc));
            },
            IntrinsicType.cast_bool => () =>
            {
                dataStack.ExpectArity(1, ArityType.any, op.loc);
                dataStack.Pop();
                dataStack.Push((TokenType._bool, op.loc));
            },
            IntrinsicType.cast_ptr => () =>
            {
                dataStack.ExpectArity(1, ArityType.any, op.loc);
                dataStack.Pop();
                dataStack.Push((TokenType._ptr, op.loc));
            },
            IntrinsicType.load32 => () =>
            {
                dataStack.ExpectArity(1, TokenType._ptr, op.loc);
                dataStack.Pop();
                dataStack.Push((TokenType._int, op.loc));
            },
            IntrinsicType.store32 => () =>
            {
                dataStack.ExpectArity(op.loc, TokenType._ptr, TokenType._any);
                dataStack.Pop();
                dataStack.Pop();
            },
            IntrinsicType.fd_write => () =>
            {
                dataStack.ExpectArity(op.loc, TokenType._ptr, TokenType._int, TokenType._ptr, TokenType._int);
                dataStack.Pop();
                dataStack.Pop();
                dataStack.Pop();
                dataStack.Pop();
                dataStack.Push((TokenType._ptr, op.loc));
            },
            _ => (Action) (() => Error(op.loc, $"Intrinsic type not implemented in `TypeCheckOp` yet: `{(IntrinsicType)op.operand}`"))
        })(),
        _ => () => Error(op.loc, $"Op type not implemented in `TypeCheckOp` yet: {op.type}")
    };

    static void ExpectArity(this DataStack stack, int arityN, ArityType arityT, Loc loc)
    {
        Assert(stack.Count >= arityN, loc, "Stack has less elements than expected");
        Assert(arityT switch
        {
            ArityType.any  => true,
            ArityType.same => ExpectArity(stack, arityN, loc),
            _ => false
        }, loc, "Arity check failed");
    }

    static bool ExpectArity(this DataStack stack, int arityN, Loc loc)
    {
        var first = stack.ElementAt(0).type;
        return ExpectArity(stack, arityN, first, loc);
    }

    static bool ExpectArity(this DataStack stack, int arityN, TokenType type, Loc loc)
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
    
    static bool ExpectArity(this DataStack stack, Loc loc, params TokenType[] contract)
    {
        Assert(stack.Count >= contract.Count(), loc, "Stack has less elements than expected");
        for (int i = 0; i < contract.Count(); i++)
        {
            var a = stack.ElementAt(i);
            var b = contract.ElementAt(i);
            if (b is not TokenType._any && !b.Equals(a.type))
            {
                Error(loc, $"Expected type `{TypeNames(b)}`, but found `{TypeNames(a.type)}`",
                    $"{a.loc} [INFO] The type found was declared here");
                return false;
            }
        }
        return true;
    }

    static bool ExpectStackExact(this DataStack stack, Loc loc, params TokenType[] contract)
    {
        Assert(stack.Count() == contract.Count(), loc,
        $"Expected stack at the end of the procedure does not match the procedure contract:",
            $"{loc} [INFO] Expected types: {ListTypes(contract.ToList())}",
            $"{loc} [INFO] Actual types:   {ListTypes(stack, true)}");
        return(stack.ExpectArity(loc, contract));
    }

    static bool ExpectStackArity(DataStack expected, DataStack actual, Loc loc, string errorText)
    {
        var check = Enumerable.SequenceEqual(expected.Select(a => a.type), actual.Select(a => a.type));
        return Assert(check, loc, errorText,
            $"{loc} [INFO] Expected types: {ListTypes(expected)}",
            $"{loc} [INFO] Actual types:   {ListTypes(actual)}");
    }

    static string ListTypes(this DataStack types) => ListTypes(types, false);
    static string ListTypes(this DataStack types, bool verbose)
    {
        var typs = ListTypes(types.Reverse().Select(t => t.type).ToList());
        if (verbose)
        {
            var sb = new StringBuilder(typs);
            sb.Append("\n");
            sb.AppendJoin('\n', types.Select(t => $"{t.loc} [INFO] Type `{TypeNames(t.type)}` was declared here"));
            return sb.ToString();
        }
        return typs;
    }

    static string ListTypes(this List<TokenType> types)
    {
        var sb = new StringBuilder("[");
        sb.AppendJoin(',',  types.Select(t => $"<{TypeNames(t)}>"));
        sb.Append("] ->");
        return sb.ToString();
    }

    public static Stack<T> Clone<T>(this Stack<T> original)
    {
        var arr = new T[original.Count];
        original.CopyTo(arr, 0);
        Array.Reverse(arr);
        return new Stack<T>(arr);
    }
}
