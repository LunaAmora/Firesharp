namespace Firesharp;

using static Parser;

static class TypeChecker
{    
    static Stack<(DataStack stack, Op op)> blockStack = new(); //TODO: This method of snapshotting the datastack is really dumb, change later
    static Stack<TypeFrame> bindStack = new();
    static DataStack dataStack = new();

    public static void TypeCheck(List<Op> program)
    {
        foreach (Op op in program) TypeCheckOp(op, program)();
    }

    static Action TypeCheckOp(Op op, List<Op> program) => op.type switch
    {
        OpType.push_int  => () => dataStack.Push(TokenType.@int, op.loc),
        OpType.push_bool => () => dataStack.Push(TokenType.@bool, op.loc),
        OpType.push_ptr  => () => dataStack.Push(TokenType.ptr, op.loc),
        OpType.push_str  => () => 
        {
            dataStack.Push(TokenType.@int, op.loc);
            dataStack.Push(TokenType.ptr, op.loc);
        },
        OpType.push_global => () => dataStack.Push(TokenType.ptr, op.loc),
        OpType.push_local  => () => dataStack.Push(TokenType.ptr, op.loc),
        OpType.unpack => () =>
        {
            var A = dataStack.ExpectArityPop(ArityType.any, op.loc);
            Assert(A.type >= TokenType.data_ptr, op.loc, $"Cannot unpack element of type: `{TypeNames(A.type)}`");
            var stk = structList[A.type - TokenType.data_ptr];
            op.operand = structList.IndexOf(stk);
            dataStack.Push(stk.members.Select(member => member.type).ToList(), op.loc);
        },
        OpType.offset => () =>
        {
            var offsetType = dataStack.ExpectStructPointer(op, ".*");
            dataStack.Push((int)TokenType.data_ptr + offsetType - (int)TokenType.@int, op.loc);
        },
        OpType.offset_load => () =>
        {
            var offsetType = dataStack.ExpectStructPointer(op, ".");
            dataStack.Push(offsetType, op.loc);
        },
        OpType.swap => () =>
        {
            var top = dataStack.ExpectArityPop(2, ArityType.any, op.loc);
            dataStack.Push(top[0]);
            dataStack.Push(top[1]);
        },
        OpType.drop => () => dataStack.ExpectArityPop(ArityType.any, op.loc),
        OpType.dup => () =>
        {
            var top = dataStack.ExpectArityPeek(TokenType.any, op.loc);
            dataStack.Push(top.type, op.loc);
        },
        OpType.rot => () =>
        {
            var top = dataStack.ExpectArityPop(3, ArityType.any, op.loc);
            dataStack.Push(top[1]);
            dataStack.Push(top[0]);
            dataStack.Push(top[2]);
        },
        OpType.over => () =>
        {
            var top = dataStack.ExpectArityPop(2, ArityType.any, op.loc);
            dataStack.Push(top[1]);
            dataStack.Push(top[0]);
            dataStack.Push(top[1]);
        },
        OpType.push_global_mem => () => dataStack.Push(TokenType.ptr, op.loc),
        OpType.push_local_mem  => () => dataStack.Push(TokenType.ptr, op.loc),
        OpType.call => () => 
        {
            if(procList[op.operand] is var (_, (ins, outs)))
            {
                var insCopy = new List<TokenType>(ins);
                insCopy.Reverse();
                dataStack.ExpectArityPop(op.loc, insCopy.ToArray());
                dataStack.Push(outs, op.loc);
            }
        },
        OpType.prep_proc => () =>
        {
            var proc = procList[op.operand];
            CurrentProc = proc;
            dataStack.Push(proc.contract.ins, op.loc);
        },
        OpType.end_proc => () =>
        {
            var outsCopy = new List<TokenType>(CurrentProc.contract.outs);
            outsCopy.Reverse();
            TokenType[] endStack = outsCopy.ToArray();

            dataStack.ExpectExactPop(op.loc, endStack);
            
            ExitCurrentProc();
            dataStack = new();
        },
        OpType.@while => () =>
        {
            blockStack.Push((dataStack, op));
            dataStack = new(dataStack);
        },
        OpType.@do => () =>
        {
            dataStack.ExpectArityPop(TokenType.@bool, op.loc);
            
            var (expected, startOp) = blockStack.Peek();

            ExpectStackArity(expected, dataStack, op.loc, 
            $"While block is not allowed to alter the types of the arguments on the data stack");
            
            blockStack.Push((dataStack, op));
            dataStack = new(expected);
        },
        OpType.end_while => () =>
        {
            var (expected, doOp) = blockStack.Pop();

            ExpectStackArity(expected, dataStack, op.loc, 
            $"Do block is not allowed to alter the types of the arguments on the data stack");
            
            var ins = Math.Min(dataStack.minCount, expected.minCount);
            var outs = Math.Max(dataStack.stackCount, expected.stackCount) - ins;

            var (oldStack, startOp) = blockStack.Pop();

            var ipDif = program.IndexOf(op) - program.IndexOf(startOp);
            startOp.operand = ipDif;
            op.operand = ipDif;

            blockContacts.Add(doOp, (-ins, outs));
            blockContacts.Add(startOp, (-ins, outs));

            dataStack.minCount   = oldStack.stackCount + ins;
            dataStack.stackCount = oldStack.stackCount;
        },
        OpType.if_start => () =>
        {
            dataStack.ExpectArityPop(TokenType.@bool, op.loc);
            blockStack.Push((dataStack, op));
            dataStack = new(dataStack);
        },
        OpType.@else => () =>
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
            var top = dataStack.ExpectArityPop(op.operand, ArityType.any, op.loc);
            for (int i = 0; i < op.operand; i++) bindStack.Push(top[i]);
        },
        OpType.push_bind => () =>
        {
            Assert(bindStack.Count > op.operand, error: "Unreachable, parser error");
            dataStack.Push(bindStack.ElementAt(op.operand).type, op.loc);
        },
        OpType.pop_bind => () =>
        {
            for (int i = 0; i < op.operand; i++) bindStack.Pop();
        },
        OpType.equal => () =>
        {
            dataStack.ExpectArityPop(2, ArityType.same, op.loc);
            dataStack.Push(TokenType.@bool, op.loc);
        },
        OpType.case_start => () =>
        {
            dataStack.ExpectArityPop(TokenType.@int, op.loc);
            blockStack.Push((dataStack, op));
            dataStack = new(dataStack);
        },
        OpType.case_option => () =>
        {
            if(op.operand is 0) return;
            var (oldStack, startOp) = blockStack.ElementAt(op.operand-1);
            blockStack.Push((dataStack, startOp));
            dataStack = new(oldStack);
        },
        OpType.end_case => () =>
        {
            var proc = CurrentProc;
            var block = proc.caseBlocks[op.operand];
            int ins = dataStack.minCount;
            int outs = dataStack.stackCount;
            Op startOp = blockStack.Peek().op;

            for (int i = 0; i < block.Count - 1; i++)
            {
                var expected = blockStack.Pop().stack;

                ExpectStackArity(expected, dataStack, op.loc, 
                "All branches of a case block must produce the same types of the arguments on the data stack");

                ins = Math.Min(ins, expected.minCount);
                outs = Math.Max(outs, expected.stackCount);
            }
            outs -= ins;

            blockContacts.Add(startOp, (1 - ins, outs));

            var oldStack = blockStack.Pop().stack;
            dataStack.minCount   = oldStack.stackCount + ins;
            dataStack.stackCount = oldStack.stackCount;
        },
        OpType.expectType => () => dataStack.ExpectArityPeek((TokenType)(op.operand), op.loc),
        OpType.intrinsic => () => ((IntrinsicType)op.operand switch
        {
            IntrinsicType.plus or
            IntrinsicType.minus => () =>
            {
                var top = dataStack.ExpectArityPop(2, ArityType.same, op.loc);
                dataStack.Push(top[1].type, op.loc);
            },
            IntrinsicType.or or
            IntrinsicType.xor or
            IntrinsicType.and => () =>
            {
                dataStack.ExpectArityPop(op.loc, TokenType.@int, TokenType.@int);
                dataStack.Push(TokenType.@int, op.loc);
            },
            IntrinsicType.lesser or
            IntrinsicType.greater or
            IntrinsicType.lesser_e or
            IntrinsicType.greater_e => () =>
            {
                dataStack.ExpectArityPop(2, ArityType.same, op.loc);
                dataStack.Push(TokenType.@bool, op.loc);
            },
            IntrinsicType.load8 or
            IntrinsicType.load16 or
            IntrinsicType.load32 => () =>
            {
                dataStack.ExpectArityPop(TokenType.ptr, op.loc);
                dataStack.Push(TokenType.@int, op.loc);
            },
            IntrinsicType.store8 or
            IntrinsicType.store16 or
            IntrinsicType.store32 => () => dataStack.ExpectArityPop(op.loc, TokenType.any, TokenType.any),
            IntrinsicType.fd_write => () =>
            {
                dataStack.ExpectArityPop(op.loc, TokenType.ptr, TokenType.@int, TokenType.ptr, TokenType.@int);
                dataStack.Push(TokenType.ptr, op.loc);
            },
            {} cast when cast >= IntrinsicType.cast => () =>
            {
                dataStack.ExpectArityPop(ArityType.any, op.loc);
                dataStack.Push(TokenType.@int + (int)(cast - IntrinsicType.cast), op.loc);
            },
            _ => (Action) (() => ErrorHere($"Intrinsic type not implemented in `TypeCheckOp` yet: `{(IntrinsicType)op.operand}`", op.loc))
        })(),
        _ => () => ErrorHere($"Op type not implemented in `TypeCheckOp` yet: `{op.type}`", op.loc)
    };

    static TokenType ExpectStructPointer(this DataStack stack, Op op, string prefix)
    {
        var A = dataStack.ExpectArityPop(ArityType.any, op.loc);
        Assert(A.type >= TokenType.data_ptr, op.loc, $"Cannot `.` access elements of type: `{TypeNames(A.type)}`");
        var word = wordList[op.operand].Split(prefix)[1];
        var stk = structList[A.type - TokenType.data_ptr];
        var index = stk.members.FindIndex(mem => mem.name.Equals(word));
        Assert(index >= 0, op.loc, $"The struct {stk.name} does not contain a member with name: `{word}`");
        op.operand = index * 4;
        return stk.members[index].type;
    }

    static TypeFrame ExpectArityPeek(this DataStack stack, TokenType type, Loc loc)
    {
        stack.ExpectArity(1, type, loc);
        return stack.Peek();
    }
    
    static void ExpectArityPop(this DataStack stack, Loc loc, params TokenType[] contract)
    {
        stack.ExpectArity(loc, contract);
        stack.Pop(contract.Count());
    }
    
    static void ExpectExactPop(this DataStack stack, Loc loc, params TokenType[] contract)
    {
        stack.typeFrames.ExpectStackExact(loc, contract);
        stack.Pop(contract.Count());
    }

    static TypeFrame ExpectArityPop(this DataStack stack, ArityType arityT, Loc loc)
    {
        stack.ExpectArity(1, arityT, loc);
        return stack.Pop();
    }

    static TypeFrame ExpectArityPop(this DataStack stack, TokenType type, Loc loc)
    {
        stack.ExpectArity(1, type, loc);
        return stack.Pop();
    }

    static TypeFrame[] ExpectArityPop(this DataStack stack, int arityN, ArityType arityT, Loc loc)
    {
        stack.ExpectArity(arityN, arityT, loc);
        return stack.GetPop(arityN);
    }

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

    static bool ExpectArity(this DataStack stack, int arityN, Loc loc)
    {
        var first = stack.ElementAt(0).type;
        return ExpectArity(stack, arityN, first, loc);
    }

    static bool ExpectArity(this DataStack stack, int arityN, TokenType type, Loc loc)
    {
        Assert(stack.Count() >= arityN, loc, "Stack has less elements than expected",
        $"{loc} [INFO] Expected `{arityN}` elements, but found `{stack.Count()}`");
        for (int i = 0; i < arityN; i++)
        {
            if(!ExpectType(stack.ElementAt(i), type, loc)) return false;
        }
        return true;
    }

    static void ExpectArity(this DataStack stack, Loc loc, params TokenType[] contract)
    {
        stack.ExpectStackSize(contract.Count(), loc);
        ExpectArity(stack.typeFrames, loc, true, contract);
    }

    static bool ExpectArity(this IEnumerable<TypeFrame> stack, Loc loc, bool panic, params TokenType[] contract)
    {
        for (int i = 0; i < contract.Count(); i++)
        {
            var stk = stack.ElementAt(i);
            var contr = contract.ElementAt(i);
            var structOffset = contr - TokenType.data_ptr;
            var anyPass = contr is TokenType.any ||
                    (structOffset >= 0 &&
                    structList.Count > structOffset && 
                    structList[structOffset].members.First().type is TokenType.any);
            if(anyPass) continue;
            if(!contr.Equals(stk.type))
            {
                Assert(!panic, loc, $"Expected type `{TypeNames(contr)}`, but found `{TypeNames(stk.type)}`",
                    $"{stk.loc} [INFO] The type found was declared here");
                return false;
            }
        }
        return true;
    }

    static void ExpectStackSize(this DataStack stack, int arityN, Loc loc)
    {
        Assert(stack.Count() >= arityN, loc, "Stack has less elements than expected",
        $"{loc} [INFO] Expected `{arityN}` elements, but found `{stack.Count()}`");
    }

    public static bool ExpectType(TypeFrame frame, TokenType expected, Loc loc)
    {
        if(expected is not TokenType.any && !expected.Equals(frame.type))
        {
            Error(loc, $"Expected type `{TypeNames(expected)}`, but found `{TypeNames(frame.type)}`",
                $"{frame.loc} [INFO] The type found was declared here");
            return false;
        }
        return true;
    }

    public static void ExpectStackExact(this IEnumerable<TypeFrame> stack, Loc loc, params TokenType[] contract)
    {
        Assert(stack.Count() == contract.Count() && stack.ExpectArity(loc, false, contract), loc,
        $"Found stack at the end of the context does match the expected types:",
            $"{loc} [INFO] Expected types: {ListTypes(contract.Reverse<TokenType>().ToList())}",
            $"{loc} [INFO] Actual types:   {ListTypes(stack, true)}");
    }

    static bool ExpectStackArity(DataStack expected, DataStack actual, Loc loc, string errorText)
    {
        var check = Enumerable.SequenceEqual(expected.typeFrames.Select(a => a.type), actual.typeFrames.Select(a => a.type));
        return Assert(check, loc, errorText,
            $"{loc} [INFO] Expected types: {ListTypes(expected)}",
            $"{loc} [INFO] Actual types:   {ListTypes(actual)}");
    }

    static string ListTypes(this DataStack types, bool verbose = false) => ListTypes(types.typeFrames.ToList(), verbose);
    public static string ListTypes(this IEnumerable<TypeFrame> types, bool verbose)
    {
        var typs = ListTypes(types.Reverse<TypeFrame>().Select(t => t.type).ToList());
        if(verbose)
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
    
    class DataStack
    {
        public Stack<TypeFrame> typeFrames = new();
        public int minCount = 0;
        public int stackCount = 0;

        public DataStack() {}
        public DataStack(DataStack dataStack)
        {
            typeFrames = dataStack.typeFrames.Clone();
            minCount = dataStack.minCount;
        }

        public void Push(List<TokenType> type, Loc loc)
        {
            for (int i = 0; i < type.Count(); i++)
                Push(type[i], loc);
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

        public void Pop(int n)
        {
            stackMinus(n);
            for (int i = 0; i < n; i++) typeFrames.Pop();
        }

        public TypeFrame[] GetPop(int n)
        {
            stackMinus(n);
            var result = new TypeFrame[n];
            for (int i = 0; i < n; i++) result[i] = typeFrames.Pop();
            return result;
        }

        public TypeFrame Peek()
        {
            stackMinus();
            stackCount++;
            return typeFrames.Peek();
        }

        public int Count() => typeFrames.Count;
        public TypeFrame ElementAt(int v) => typeFrames.ElementAt(v);

        void stackMinus(int n = 1)
        {
            stackCount -= 1;
            if(stackCount < minCount) minCount = stackCount;
        }
    }
    
    enum ArityType
    {
        any,
        same
    }
}
