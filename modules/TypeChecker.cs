namespace Firesharp;

using static Tokenizer;
using static Parser;

static class TypeChecker
{
    public static Dictionary<Op, (int, int)> blockContacts = new();
    
    static DataStack dataStack = new();
    static Stack<(DataStack stack, Op op)> blockStack = new(); //TODO: This method of snapshotting the datastack is really dumb, change later
    static Stack<TypeFrame> bindStack = new();

    public static void TypeCheck(List<Op> program)
    {
        foreach (Op op in program) TypeCheckOp(op)();
    }

    static Action TypeCheckOp(Op op) => op.type switch
    {
        OpType.push_int  => () => dataStack.Push(TokenType._int, op.loc),
        OpType.push_bool => () => dataStack.Push(TokenType._bool, op.loc),
        OpType.push_ptr  => () => dataStack.Push(TokenType._ptr, op.loc),
        OpType.push_str  => () => 
        {
            dataStack.Push(TokenType._int, op.loc);
            dataStack.Push(TokenType._ptr, op.loc);
        },
        OpType.push_global => () => dataStack.Push(TokenType._ptr, op.loc),
        OpType.push_local => () =>
        {
            Assert(InsideProc, "Unreachable, parser error.");
            dataStack.Push(TokenType._ptr, op.loc);
        },
        OpType.offset => () =>
        {
            dataStack.ExpectArity(1, ArityType.any, op.loc);
            var A = dataStack.Pop();
            Assert(A.type >= TokenType._struct, op.loc, $"Cannot `.` access elements of type: `{TypeNames(A.type)}`");
            var word = wordList[op.operand].Split(".*")[1];
            var stk = structList[A.type - TokenType._struct];
            var index = stk.members.FindIndex(mem => mem.name.Equals(word));
            Assert(index >= 0, op.loc, $"The struct {stk.name} does not contain a member with name: `{word}`");
            op.operand = index * 4;
            dataStack.Push((int)TokenType._struct + stk.members[index].type - (int)TokenType._int, op.loc);
        },
        OpType.offset_load => () =>
        {
            dataStack.ExpectArity(1, ArityType.any, op.loc);
            var A = dataStack.Pop();
            Assert(A.type >= TokenType._struct, op.loc, $"Cannot `.` access elements of type: `{TypeNames(A.type)}`");
            var word = wordList[op.operand].Split('.')[1];
            var stk = structList[A.type - TokenType._struct];
            var index = stk.members.FindIndex(mem => mem.name.Equals(word));
            Assert(index >= 0, op.loc, $"The struct {stk.name} does not contain a member with name: `{word}`");
            op.operand = index * 4;
            dataStack.Push(stk.members[index].type, op.loc);
        },
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
            dataStack.Push(dataStack.Peek().type, op.loc);
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
        OpType.push_global_mem => () => dataStack.Push(TokenType._ptr, op.loc),
        OpType.push_local_mem => () => dataStack.Push(TokenType._ptr, op.loc),
        OpType.call => () => 
        {
            if (procList[op.operand] is var (_, (ins, outs)))
            {
                var insCopy = new List<TokenType>(ins);
                insCopy.Reverse();
                dataStack.ExpectArity(op.loc, insCopy.ToArray());
                insCopy.ForEach(_ => dataStack.Pop());
                for (int i = 0; i < outs.Count; i++) dataStack.Push(outs[i], op.loc);
            }
        },
        OpType.prep_proc => () =>
        {
            Assert(!InsideProc, "Unreachable, parser error.");
            CurrentProc = procList[op.operand];
            CurrentProc.contract.ins.ForEach(type => dataStack.Push(type, op.loc));
        },
        OpType.end_proc => () =>
        {
            Assert(InsideProc, "Unreachable, parser error.");
            var outsCopy = new List<TokenType>(CurrentProc.contract.outs);
            outsCopy.Reverse();
            TokenType[] endStack = outsCopy.ToArray();

            dataStack.ExpectStackExact(op.loc, endStack);
            foreach (var _ in endStack) dataStack.Pop();
            
            ExitCurrentProc();
            dataStack = new();
        },
        OpType.if_start => () =>
        {
            dataStack.ExpectArity(1, TokenType._bool, op.loc);
            dataStack.Pop();
            blockStack.Push((dataStack, op));
            dataStack = new(dataStack);
        },
        OpType._else => () =>
        {
            var (oldStack, startOp) = blockStack.Peek();
            blockStack.Push((dataStack, startOp));
            dataStack = new(oldStack);
        },
        OpType.end_if => () =>
        {
            var (expected, startOp) = blockStack.Pop();

            ExpectStackArity(expected, dataStack, op.loc, 
            $"Else-less if block is not allowed to alter the types of the arguments on the data stack");
            
            var ins = Math.Abs(dataStack.minCount);
            var outs = ins + dataStack.stackCount;
            blockContacts.Add(startOp, (ins, outs));
            
            dataStack.minCount   = expected.stackCount + dataStack.minCount;
            dataStack.stackCount = expected.stackCount;
        },
        OpType.end_else => () =>
        {
            var (expected, startOp) = blockStack.Pop();

            ExpectStackArity(expected, dataStack, op.loc, 
            $"Both branches of the if-block must produce the same types of the arguments on the data stack");
            var ins = Math.Min(dataStack.minCount, expected.minCount);
            var outs = Math.Max(dataStack.stackCount, expected.stackCount) - ins;
            blockContacts.Add(startOp, (-ins, outs));

            var oldStack = blockStack.Pop().stack;
            dataStack.minCount   = oldStack.stackCount + ins;
            dataStack.stackCount = oldStack.stackCount;
        },
        OpType.bind_stack => () =>
        {
            dataStack.ExpectArity(op.operand, ArityType.any, op.loc);
            var elements = new List<TypeFrame>();
            for (int i = 0; i < op.operand; i++)
            {
                elements.Add(dataStack.Pop());
            }
            elements.ForEach(type => bindStack.Push(type));
        },
        OpType.push_bind => () =>
        {
            Assert(bindStack.Count > op.operand, "Unreachable, parser error");
            dataStack.Push(bindStack.ElementAt(op.operand).type, op.loc);
        },
        OpType.pop_bind => () =>
        {
            for (int i = 0; i < op.operand; i++)
            {
                bindStack.Pop();
            }
        },
        OpType.equal => () =>
        {
            dataStack.ExpectArity(2, ArityType.same, op.loc);
            dataStack.Pop();
            dataStack.Pop();
            dataStack.Push(TokenType._bool, op.loc);
        },
        OpType.intrinsic => () => ((IntrinsicType)op.operand switch
        {
            IntrinsicType.plus => () =>
            {
                dataStack.ExpectArity(2, ArityType.same, op.loc);
                dataStack.Pop();
                dataStack.Push(dataStack.Pop().type, op.loc);
            },
            IntrinsicType.minus => () =>
            {
                dataStack.ExpectArity(2, ArityType.same, op.loc);
                dataStack.Pop();
                dataStack.Push(dataStack.Pop().type, op.loc);
            },
            IntrinsicType.load32 => () =>
            {
                dataStack.ExpectArity(1, TokenType._ptr, op.loc);
                dataStack.Pop();
                dataStack.Push(TokenType._int, op.loc);
            },
            IntrinsicType.store32 => () =>
            {
                dataStack.ExpectArity(op.loc, TokenType._any, TokenType._any);
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
                dataStack.Push(TokenType._ptr, op.loc);
            },
            {} cast when cast >= IntrinsicType.cast => () =>
            {
                dataStack.ExpectArity(1, ArityType.any, op.loc);
                var A = dataStack.Pop();
                dataStack.Push(TokenType._int + (int)(cast - IntrinsicType.cast), op.loc);
                // Info($"Casting {A.type} to {TypeNames(TokenType._int + (int)(cast - IntrinsicType.cast))}");
            },
            _ => (Action) (() => Error(op.loc, $"Intrinsic type not implemented in `TypeCheckOp` yet: `{(IntrinsicType)op.operand}`"))
        })(),
        _ => () => Error(op.loc, $"Op type not implemented in `TypeCheckOp` yet: {op.type}")
    };

    static void ExpectArity(this DataStack stack, int arityN, ArityType arityT, Loc loc)
    {
        stack.ExpectStackSize(arityN, loc);
        Assert(arityT switch
        {
            ArityType.any  => true,
            ArityType.same => ExpectArity(stack, arityN, loc),
            _ => false
        }, loc, "Arity check failed");
    }

    static void ExpectStackSize(this DataStack stack, int arityN, Loc loc)
    {
        Assert(stack.Count() >= arityN, loc, "Stack has less elements than expected",
        $"{loc} [INFO] Expected `{arityN}` elements, but found `{stack.Count()}`");
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
            if (!ExpectType(stack.ElementAt(i), type, loc)) return false;
        }
        return true;
    }

    public static bool ExpectType(TypeFrame frame, TokenType expected, Loc loc)
    {
        if (!expected.Equals(frame.type))
        {
            Error(loc, $"Expected type `{TypeNames(expected)}`, but found `{TypeNames(frame.type)}`",
                $"{frame.loc} [INFO] The type found was declared here");
            return false;
        }
        return true;
    }

    static bool ExpectArity(this DataStack stack, Loc loc, params TokenType[] contract)
    {
        stack.ExpectStackSize(contract.Count(), loc);
        return ExpectArity(stack.typeFrames, loc, contract);
    }

    static bool ExpectArity(this IEnumerable<TypeFrame> stack, Loc loc, params TokenType[] contract)
    {
        for (int i = 0; i < contract.Count(); i++)
        {
            var stk = stack.ElementAt(i);
            var contr = contract.ElementAt(i);
            var structOffset = contr - TokenType._struct;
            var anyPass = contr is not TokenType._any &&
                    structOffset >= 0 &&
                    structList.Count > structOffset && 
                    structList[structOffset].members.First().type is not TokenType._any;

            if (anyPass && !contr.Equals(stk.type))
            {
                Error(loc, $"Expected type `{TypeNames(contr)}`, but found `{TypeNames(stk.type)}`",
                    $"{stk.loc} [INFO] The type found was declared here");
                return false;
            }
        }
        return true;
    }

    static bool ExpectStackExact(this DataStack stack, Loc loc, params TokenType[] contract)
        => ExpectStackExact(stack.typeFrames, loc, contract);

    public static bool ExpectStackExact(this IEnumerable<TypeFrame> stack, Loc loc, params TokenType[] contract)
    {
        // Info(loc, $"eval types:   {ListTypes(stack, false)}");
        Assert(stack.Count() == contract.Count(), loc,
        $"Expected stack at the end of the procedure does not match the procedure contract:",
            $"{loc} [INFO] Expected types: {ListTypes(contract.ToList())}",
            $"{loc} [INFO] Actual types:   {ListTypes(stack, true)}");
        return(stack.ExpectArity(loc, contract));
    }

    static bool ExpectStackArity(DataStack expected, DataStack actual, Loc loc, string errorText)
    {
        var check = Enumerable.SequenceEqual(expected.typeFrames.Select(a => a.type), actual.typeFrames.Select(a => a.type));
        return Assert(check, loc, errorText,
            $"{loc} [INFO] Expected types: {ListTypes(expected)}",
            $"{loc} [INFO] Actual types:   {ListTypes(actual)}");
    }

    static string ListTypes(this DataStack types) => ListTypes(types.typeFrames.ToList(), false);
    static string ListTypes(this DataStack types, bool verbose) => ListTypes(types.typeFrames.ToList(), verbose);
    public static string ListTypes(this IEnumerable<TypeFrame> types, bool verbose)
    {
        var typs = ListTypes(types.Reverse<TypeFrame>().Select(t => t.type).ToList());
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

    static Stack<T> Clone<T>(this Stack<T> original)
    {
        var arr = new T[original.Count];
        original.CopyTo(arr, 0);
        Array.Reverse(arr);
        return new Stack<T>(arr);
    }

    public record struct TypeFrame(TokenType type, Loc loc)
    {
        public static implicit operator TypeFrame((TokenType type, Loc loc) value)
            => new TypeFrame(value.type, value.loc);
        public static implicit operator TypeFrame(IRToken value)
            => new TypeFrame(value.type, value.loc);
    }
    
    struct DataStack
    {
        public Stack<TypeFrame> typeFrames = new();
        public int minCount = 0;
        public int stackCount = 0;

        public DataStack() {}
        public DataStack(DataStack dataStack)
        {
            typeFrames = dataStack.typeFrames.Clone();
        }

        public void Push(TokenType type, Loc loc) => Push((type, loc));
        public void Push(TypeFrame typeFrame)
        {
            stackCount++;
            typeFrames.Push(typeFrame);
        }

        public TypeFrame Pop()
        {
            stackMinus();
            return typeFrames.Pop();
        }

        public TypeFrame Peek()
        {
            stackMinus();
            stackCount++;
            return typeFrames.Peek();
        }

        public int Count() => typeFrames.Count;
        public TypeFrame ElementAt(int v) => typeFrames.ElementAt(v);

        void stackMinus()
        {
            stackCount--;
            if (stackCount < minCount) minCount = stackCount;
        }
    }
    
    enum ArityType
    {
        any,
        same
    }
}
