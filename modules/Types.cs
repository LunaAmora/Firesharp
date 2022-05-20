namespace Firesharp;

static partial class Firesharp
{
    struct Contract
    {
        public List<DataType> ins = new List<DataType>();
        public List<DataType> outs = new List<DataType>();
        
        public Contract(List<DataType> ins) => this.ins = ins;
        public Contract(params DataType[] ins) => this.ins.AddRange(ins);

        public Contract(DataType[] ins, DataType[] outs) : this(ins) => this.outs.AddRange(outs);
        public Contract(List<DataType> ins, List<DataType> outs)
        {
            this.ins = ins;
            this.outs = outs;
        }
    }

    struct Proc
    {
        public Contract contract;
        public Queue<Op> procOps = new Queue<Op>();

        public Proc() => contract = default;
        public Proc(Contract contract) => this.contract = contract;
    }
    
    abstract class Op 
    {
        public abstract void Check();
        public abstract void Generate();

        public static Op New<T>(T type)
            where T : struct, Enum => new Op<T>(type);

        public static Op New<T>(T type, int operand)
            where T : struct, Enum => new Op<T>(type, operand);
    }

    class Op<t> : Op
        where t : struct, Enum
    {
        public t Type;
        public int Operand;

        public Op(t type) => Type = type;
        public Op(t type, int operand) : this(type) => Operand = operand;

        public override void Check() => TypeCheckOp(this)();
        public override void Generate() => GenerateOp(this)();
    }

    enum DataType
    {
        _int,
        _bool,
        _str,
        _cstr,
        _ptr,
        _any
    }

    enum ArityType
    {
        any,
        same
    }

    enum ActionType
    {
        push_int,
        push_bool,
        push_ptr,
        push_str,
        push_cstr,
        dup,
        drop,
        swap,
        over,
        rot
    }

    enum IntrinsicType
    {
        plus,
        minus,
        times,
        div,
        dump,
        equal,
        call
    }

    enum KeywordType
    {
        proc,
        end,
    }

    enum IntermediateType
    {
        Word,
        Keyword,
        Action,
        Intrinsic,
    }
}