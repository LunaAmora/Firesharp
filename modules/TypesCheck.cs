using System.Diagnostics;

namespace Firesharp;

static partial class Firesharp
{
    static void TypeCheck(Queue<Op> program)
    {
        while(program.TryDequeue(out var current))
        {
            current.Check();
        }
    }

    static Action TypeCheckOp<opType>(Op<opType> op)
        where opType : struct, Enum => op.Type switch
    {
        ActionType.push_int  => () => dataStack.Push(DataType._int),
        ActionType.push_bool => () => dataStack.Push(DataType._bool),
        ActionType.push_ptr  => () => dataStack.Push(DataType._ptr),
        ActionType.push_str  => () => dataStack.Push(DataType._str),
        ActionType.push_cstr => () => dataStack.Push(DataType._cstr),
        ActionType.drop => () =>
        {
            dataStack.ExpectArity(1, ArityType.any);
            dataStack.Pop();
        },
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
        IntrinsicType.dump => () =>
        {
            dataStack.ExpectArity(1, ArityType.any);
            dataStack.Pop();
        },
        IntrinsicType.call => () =>
        {
            Debug.Assert(procList.Count > op.Operand, "[Error] Proclist does not contain the needed proc id");
            Proc proc = procList.ElementAt(op.Operand);
            dataStack.ExpectArity(proc.contract);
            TypeCheck(proc.procOps);
        },
        _ => () => Debug.Assert(false, $"[Error] Op type not implemented in typechecking: {op.Type.ToString()}")
    };

    static void ExpectArity(this Stack<DataType> stack, Contract contract)
    {
        var arity = true;
        var count = contract.ins.Count() - 1;

        Debug.Assert(stack.Count > count, "[Error] Stack has less elements than expected");

        for (int i = 0; i <= count; i++)
        {
            arity &= stack.ElementAt(i).Equals(contract.ins[count - i]);
        }
        
        Debug.Assert(arity, "[Error] Arity check failled");
    }

    static void ExpectArity(this Stack<DataType> stack, int arityN, ArityType arityT)
    {
        Debug.Assert(stack.Count >= arityN, "[Error] Stack has less elements than expected");
        Func<bool> arity = arityT switch
        {
            ArityType.any  => () => true,
            ArityType.same => () =>
            {
                var type = stack.ElementAt(0);
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
        
        Debug.Assert(arity(), "[Error] Arity check failled");
    }
}