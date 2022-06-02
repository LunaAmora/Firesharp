namespace Firesharp;

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
        OpType.push_int  => () => dataStack.Push(TokenType._int, op.loc),
        OpType.push_bool => () => dataStack.Push(TokenType._bool, op.loc),
        OpType.push_ptr  => () => dataStack.Push(TokenType._ptr, op.loc),
        OpType.push_str  => () => 
        {
            dataStack.Push(TokenType._int, op.loc);
            dataStack.Push(TokenType._ptr, op.loc);
        },
        OpType.push_cstr => () => dataStack.Push(TokenType._ptr, op.loc),
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
            if (procList[op.operand].contract is Contract contr)
            {
                var ins = new List<TokenType>(contr.ins);
                ins.Reverse();
                dataStack.ExpectArity(op.loc, ins.ToArray());
                ins.ForEach(_ => dataStack.Pop());
                var outs = contr.outs;
                for (int i = 0; i < outs.Count; i++) dataStack.Push(outs[i], op.loc);
            }
        },
        OpType.prep_proc => () =>
        {
            currentProc = procList[op.operand];
            if (currentProc.contract is Contract contr)
            {
                contr.ins.ForEach(type => dataStack.Push(type, op.loc));
            }
        },
        OpType.end_proc => () =>
        {
            TokenType[] endStack;
            if(currentProc is Proc proc && proc.contract is Contract contr)
            {
                var ou = contr.outs;
                ou.Reverse();
                endStack = ou.ToArray();
            }
            else endStack = new TokenType[0];

            dataStack.ExpectStackExact(op.loc, endStack);
            foreach (var _ in endStack) dataStack.Pop();
            
            currentProc = null;
            dataStack = new();
        },
        OpType.if_start => () =>
        {
            dataStack.ExpectArity(1, TokenType._bool, op.loc);
            dataStack.Pop();
            blockStack.Push((dataStack, op));
            dataStack = new (dataStack);
        },
        OpType._else => () =>
        {
            (var oldStack, var startOp) = blockStack.Pop();
            blockStack.Push((dataStack, startOp));
            dataStack = new (oldStack);
        },
        OpType.end_if => () =>
        {
            (var expected, var startOp) = blockStack.Pop();

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
            (var expected, var startOp) = blockStack.Pop();

            ExpectStackArity(expected, dataStack, op.loc, 
            $"Both branches of the if-block must produce the same types of the arguments on the data stack");
            
            var ins = Math.Abs(Math.Min(dataStack.minCount, expected.minCount));
            var outs = ins + Math.Max(dataStack.stackCount, expected.stackCount);
            blockContacts.Add(startOp, (ins, outs));
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
            IntrinsicType.equal => () =>
            {
                dataStack.ExpectArity(2, ArityType.same, op.loc);
                dataStack.Pop();
                dataStack.Pop();
                dataStack.Push(TokenType._bool, op.loc);
            },
            IntrinsicType.cast_bool => () =>
            {
                dataStack.ExpectArity(1, ArityType.any, op.loc);
                dataStack.Pop();
                dataStack.Push(TokenType._bool, op.loc);
            },
            IntrinsicType.cast_ptr => () =>
            {
                dataStack.ExpectArity(1, ArityType.any, op.loc);
                dataStack.Pop();
                dataStack.Push(TokenType._ptr, op.loc);
            },
            IntrinsicType.load32 => () =>
            {
                dataStack.ExpectArity(1, TokenType._ptr, op.loc);
                dataStack.Pop();
                dataStack.Push(TokenType._int, op.loc);
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
                dataStack.Push(TokenType._ptr, op.loc);
            },
            _ => (Action) (() => Error(op.loc, $"Intrinsic type not implemented in `TypeCheckOp` yet: `{(IntrinsicType)op.operand}`"))
        })(),
        _ => () => Error(op.loc, $"Op type not implemented in `TypeCheckOp` yet: {op.type}")
    };

    static void ExpectArity(this DataStack stack, int arityN, ArityType arityT, Loc loc)
    {
        Assert(stack.Count() >= arityN, loc, "Stack has less elements than expected");
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
        Assert(stack.Count() >= contract.Count(), loc, "Stack has less elements than expected");
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
        var check = Enumerable.SequenceEqual(expected.typeFrames.Select(a => a.type), actual.typeFrames.Select(a => a.type));
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
            sb.AppendJoin('\n', types.typeFrames.Select(t => $"{t.loc} [INFO] Type `{TypeNames(t.type)}` was declared here"));
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

    struct DataStack
    {
        public Stack<TypeFrame> typeFrames = new Stack<TypeFrame>();
        public int minCount = 0;
        public int stackCount = 0;

        public DataStack() {}
        public DataStack(DataStack dataStack)
        {
            typeFrames = dataStack.typeFrames.Clone();
        }

        public void Push(TokenType type, Loc loc) => Push(new (type, loc));
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
        public IEnumerable<TypeFrame> Reverse() => typeFrames.Reverse();

        void stackMinus()
        {
            stackCount--;
            if (stackCount < minCount) minCount = stackCount;
        }
    }

    struct TypeFrame
    {
        public TokenType type;
        public Loc loc;

        public TypeFrame(TokenType type, Loc loc)
        {
            this.type = type;
            this.loc = loc;
        }
    }
}
