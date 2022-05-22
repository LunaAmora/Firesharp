namespace Firesharp;

static partial class Firesharp
{
    static Stack<DataType> dataStack = new Stack<DataType>();

    static void TypeCheck(List<Op> program)
    {
        foreach (Op op in program)
        {
            TypeCheckOp(op)();
        }
    }

    static Action TypeCheckOp(Op op)  => op.Type switch
    {
        OpType.push_int  => () => dataStack.Push(DataType._int),
        OpType.push_bool => () => dataStack.Push(DataType._bool),
        OpType.push_ptr  => () => dataStack.Push(DataType._ptr),
        OpType.push_str  => () => dataStack.Push(DataType._str),
        OpType.push_cstr => () => dataStack.Push(DataType._cstr),
        OpType.swap => () =>
        {
            dataStack.ExpectArity(2, ArityType.any);
        },
        OpType.drop => () =>
        {
            dataStack.ExpectArity(1, ArityType.any);
            dataStack.Pop();
        },
        OpType.intrinsic => () => ((IntrinsicType)op.Operand switch
        {
            IntrinsicType.plus => () =>
            {
                dataStack.ExpectArity(2, ArityType.same);
                dataStack.Pop();
            },
            IntrinsicType.minus => () =>
            {
                dataStack.ExpectArity(2, ArityType.same);
                dataStack.Pop();
            },
            IntrinsicType.equal => () =>
            {
                dataStack.ExpectArity(2, ArityType.same);
                dataStack.Pop();
                dataStack.Pop();
                dataStack.Push(DataType._bool);
            },
            _ => (Action) (() => Error($"intrinsic value `{(IntrinsicType)op.Operand}`is not valid or is not implemented"))
        })(),
        OpType.call => () =>
        {
            Assert(procList.Count > op.Operand, "Proclist does not contain the needed proc id");
            Proc proc = procList.ElementAt(op.Operand);
            dataStack.ExpectArity(proc.contract);
            TypeCheck(proc.procOps);
        },
        _ => () => Error($"Op type not implemented in typechecking: {op.Type.ToString()}")
    };

    static void ExpectArity(this Stack<DataType> stack, Contract contract)
    {
        bool arity = true;
        int count = contract.ins.Count() - 1;

        Assert(stack.Count > count, "Stack has less elements than expected");

        for (int i = 0; i <= count; i++)
        {
            arity &= stack.ElementAt(i).Equals(contract.ins[count - i]);
        }
        
        Assert(arity, "Arity check failled");
    }

    static void ExpectArity(this Stack<DataType> stack, int arityN, ArityType arityT)
    {
        Assert(stack.Count >= arityN, "Stack has less elements than expected");
        Func<bool> arity = arityT switch
        {
            ArityType.any  => () => true,
            ArityType.same => () =>
            {
                DataType type = stack.ElementAt(0);
                for(int i = 0; i < arityN - 1; ++i)
                {
                    if(!type.Equals(stack.ElementAt(i)))
                    {
                        return false;
                    }
                }
                return true;
            },
            _ => () => false
        };
        
        Assert(arity(), "Arity check failled");
    }
}