namespace Firesharp;

static partial class Firesharp
{
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

    public enum DataType
    {
        _int,
        _bool,
        _str,
        _cstr,
        _ptr
    }

    static string DataTypeName(this DataType type) => type switch
    {
        DataType._int  => "Integer",
        DataType._bool => "Boolean",
        DataType._str  => "String",
        DataType._cstr => "C-style String",
        DataType._ptr  => "Pointer",
        _ => Error($"dataType name not implemented: {type}")
    };

    static string ListTypes(this List<DataType> types)
    {
        return $"[{string.Join(',', types.Select(typ => $"<{DataTypeName(typ)}>").ToList())}]";
    }

    struct Token
    {
        public string name;
        public Loc loc;

        public Token(string tokenName, string file, int line, int col)
        {
            name = tokenName;
            loc = new (file, line, col);
        }
    }
    
    struct IRToken
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
        dup,
        drop,
        swap,
        over,
        rot,
        if_start,
        _else,
        end_if,
        end_else,
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
        _if,
        _else,
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