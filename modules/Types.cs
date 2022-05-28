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

        public override string ToString() => $"{file}:{line}:{col}:";
    }

    public enum TokenType
    {
        _int,
        _bool,
        _str,
        _cstr,
        _ptr,
        _word,
        _keyword
    }

    static string TypeNames(this TokenType type) => type switch
    {
        TokenType._int  => "Integer",
        TokenType._bool => "Boolean",
        TokenType._str  => "String",
        TokenType._cstr => "C-style String",
        TokenType._ptr  => "Pointer",
        TokenType._word => "Word",
        TokenType._keyword => "Keyword",
        _ => Error($"DataType name not implemented: {type}")
    };

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
        public TokenType Type;
        public int Operand = 0;

        public IRToken(TokenType type, Loc loc)
        {
            Type = type;
            Loc = loc;
        }
        
        public IRToken(TokenType type, int operand, Loc loc) : this(type, loc) => Operand = operand;
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

    enum ArityType
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
        push_global_mem,
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
        memory,
        dup,
        drop,
        swap,
        over,
        rot,
    }
}