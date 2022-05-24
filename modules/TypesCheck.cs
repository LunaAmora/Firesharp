namespace Firesharp;

static partial class Firesharp
{
    static Stack<TypeLoc> dataStack = new ();

    static void TypeCheck(List<Op> program)
    {
        foreach (Op op in program)
        {
            TypeCheckOp(op)();
        }
    }

    static Action TypeCheckOp(Op op)  => op.Type switch
    {
        OpType.push_int  => () => dataStack.Push(new (DataType._int,  op.Loc)),
        OpType.push_bool => () => dataStack.Push(new (DataType._bool, op.Loc)),
        OpType.push_ptr  => () => dataStack.Push(new (DataType._ptr,  op.Loc)),
        OpType.push_str  => () => dataStack.Push(new (DataType._str,  op.Loc)),
        OpType.push_cstr => () => dataStack.Push(new (DataType._cstr, op.Loc)),
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
            dataStack.Push(dataStack.Peek());
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
        OpType.intrinsic => () => ((IntrinsicType)op.Operand switch
        {
            IntrinsicType.plus => () =>
            {
                dataStack.ExpectArity(2, ArityType.same, op.Loc);
                dataStack.Pop();
            },
            IntrinsicType.minus => () =>
            {
                dataStack.ExpectArity(2, ArityType.same, op.Loc);
                dataStack.Pop();
            },
            IntrinsicType.equal => () =>
            {
                dataStack.ExpectArity(2, ArityType.same, op.Loc);
                dataStack.Pop();
                dataStack.Pop();
                dataStack.Push(new (DataType._bool, op.Loc));
            },
            _ => (Action) (() => Error(op.Loc, $"intrinsic value `{(IntrinsicType)op.Operand}`is not valid or is not implemented"))
        })(),
        OpType.call => () =>
        {
            Assert(procList.Count > op.Operand, op.Loc, "Proclist does not contain the needed proc id");
            Proc proc = procList.ElementAt(op.Operand);
            dataStack.ExpectArity(proc.contract, op.Loc);
            TypeCheck(proc.procOps);
            Error(op.Loc, "OpType.call outputs are not typechecked yet");
        },
        _ => () => Error(op.Loc, $"Op type not implemented in typechecking: {op.Type.ToString()}")
    };

    static void ExpectArity(this Stack<TypeLoc> stack, Contract contract, Loc loc)
    {
        int count = contract.inTypes.Count() - 1;
        Assert(stack.Count > count, loc, "Stack has less elements than expected");

        for (int i = 0; i <= count; i++)
        {
            if (!stack.ElementAt(i).Type.Equals(contract.inTypes[count - i]))
            {
                Error(loc, "Arity check failled");
                return;
            }
        }
    }

    static void ExpectArity(this Stack<TypeLoc> stack, int arityN, ArityType arityT, Loc loc)
    {
        Assert(stack.Count >= arityN, loc, "Stack has less elements than expected");
        Assert(arityT switch
        {
            ArityType.any  => true,
            ArityType.same => ExpectSame(stack, arityN),
            _ => false
        }, loc, "Arity check failled");
    }

    static bool ExpectSame(Stack<TypeLoc> stack, int arityN)
    {
        var first = stack.ElementAt(0).Type;
        for (int i = 0; i < arityN - 1; ++i)
        {
            if (!first.Equals(stack.ElementAt(i).Type))
            {
                return false;
            }
        }
        return true;
    }
}