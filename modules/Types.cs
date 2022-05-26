namespace Firesharp;

static class Types
{
    public struct Contract
    {
        public List<DataType> inTypes = new ();
        public List<DataType> outTypes = new ();
        
        public Contract(List<DataType> ins) => inTypes = ins;
        public Contract(params DataType[] ins) => inTypes.AddRange(ins);

        public Contract(List<DataType> ins, List<DataType> outs) : this(ins) => outTypes = outs;
        public Contract(DataType[] ins, DataType[] outs) : this(ins) => outTypes.AddRange(outs);
    }

    public struct Proc
    {
        public Contract contract;
        public List<Op> procOps = new ();

        public Proc() => contract = default;
        public Proc(Contract contract) => this.contract = contract;
    }

    public struct Loc
    {
        public string file;
        public int line;
        public int col;

        public Loc(string filename, int lineNum, int colNum)
        {
            file = filename;
            line = lineNum;
            col = colNum;
        }

        public override string? ToString() => $"{file}:{line}:{col}:";
    }

    public struct Token
    {
        public string name;
        public Loc loc;

        public Token(string tokenName, string file, int line, int col)
        {
            name = tokenName;
            loc = new (file, line, col);
        }
    }
    
    public struct IRToken
    {
        public Loc Loc;
        public Enum Type;
        public int Operand = 0;

        public IRToken(Enum type, Loc loc)
        {
            Type = type;
            Loc = loc;
        }
        
        public IRToken(Enum type, int operand, Loc loc) : this(type, loc) => Operand = operand;
    }
    
    public struct Op 
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

    public enum DataType
    {
        _int,
        _bool,
        _str,
        _cstr,
        _ptr,
        _any
    }

    public enum ArityType
    {
        any,
        same
    }

    public enum OpType
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
        if_start,
        end_if,
    }

    public enum IntrinsicType
    {
        plus,
        minus,
        times,
        div,
        equal,
    }

    public enum KeywordType
    {
        _if,
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