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
        public List<Op> procOps = new List<Op>();

        public Proc() => contract = default;
        public Proc(Contract contract) => this.contract = contract;
    }

    struct Loc
    {
        int line;
        int pos;

        public Loc(int line, int pos)
        {
            this.line = line;
            this.pos = pos;
        }
    }
    
    abstract class Op 
    {
        public abstract void Check();
        public abstract void Generate();
        public Loc Loc;

        public static Op New<T>(T type, Loc loc)
            where T : struct, Enum => new Op<T>(type, loc);

        public static Op New<T>(T type, int operand, Loc loc)
            where T : struct, Enum => new Op<T>(type, operand, loc);
    }

    class Op<t> : Op
        where t : struct, Enum
    {
        public t Type;
        public int Operand;

        public Op(t type, Loc loc)
        {
            Type = type;
            Loc = loc;
        }
        
        public Op(t type, int operand, Loc loc) : this(type, loc) => Operand = operand;

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

    enum OpType
    {
        push_int,
        push_bool,
        push_ptr,
        push_str,
        push_cstr,
        call,
        dup,
        drop,
        swap,
        over,
        rot,
    }

    enum IntrinsicType
    {
        plus,
        minus,
        times,
        div,
        equal,
    }

    enum KeywordType
    {
        proc,
        _in,
        end,
        dup,
        drop,
        swap,
        over,
        rot,
    }
}