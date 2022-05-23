namespace Firesharp;

static partial class Firesharp
{
    struct Contract
    {
        public List<DataType> inTypes = new List<DataType>();
        public List<DataType> outTypes = new List<DataType>();
        
        public Contract(List<DataType> ins) => inTypes = ins;
        public Contract(params DataType[] ins) => inTypes.AddRange(ins);

        public Contract(List<DataType> ins, List<DataType> outs) : this(ins) => outTypes = outs;
        public Contract(DataType[] ins, DataType[] outs) : this(ins) => outTypes.AddRange(outs);
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
        public int line;
        public int col;

        public Loc(int line, int col)
        {
            this.line = line;
            this.col = col;
        }
    }

    struct Token
    {
        public string name;
        public Loc loc;

        public Token(string name, int line, int col)
        {
            this.name = name;
            loc = new Loc(line, col);
        }
    }
    
    struct Op 
    {
        public Loc Loc;
        public OpType Type;
        public int Operand = 0;

        public Op(OpType type, Loc loc)
        {
            Type = type;
            Loc = loc;
        }

        public Op(OpType type, int operand, Loc loc)
        {
            Loc = loc;
            Type = type;
            Operand = operand;
        }
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
        intrinsic,
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