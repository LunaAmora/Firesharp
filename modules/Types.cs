namespace Firesharp;

using DataList = List<(string name, int offset)>;

static partial class Firesharp
{
    record Proc(string name, Contract? contract)
    {
        public DataList localMemNames = new();
        public int procMemSize = 0;
    }

    record Contract(List<TokenType> ins, List<TokenType> outs);

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

    struct IRToken
    {
        public TokenType Type;
        public int Operand;
        public Loc Loc;

        public IRToken(TokenType type, int operand, Loc loc)
        {
            Loc = loc;
            Type = type;
            Operand = operand;
        }
    }
    
    public struct Op 
    {
        public OpType type;
        public int operand = 0;
        public Loc loc;

        public Op(OpType Type, Loc Loc)
        {
            type = Type;
            loc = Loc;
        }

        public Op(OpType Type, int Operand, Loc Loc)
        {
            loc = Loc;
            type = Type;
            operand = Operand;
        }

        public static explicit operator Op?(string str) => null;
    }
    
    public enum TokenType
    {
        _int,
        _bool,
        _str,
        _ptr,
        _word,
        _keyword,
        _any
    }

    static string TypeNames(this TokenType type) => type switch
    {
        TokenType._int  => "Integer",
        TokenType._bool => "Boolean",
        TokenType._str  => "String",
        TokenType._ptr  => "Pointer",
        TokenType._word => "Word",
        TokenType._any  => "Any",
        TokenType._keyword => "Keyword",
        _ => Error($"DataType name not implemented: {type}")
    };
    
    public enum OpType
    {
        push_int,
        push_bool,
        push_ptr,
        push_str,
        push_local_mem,
        push_global_mem,
        global_var,
        store_var,
        load_var,
        intrinsic,
        dup,
        drop,
        swap,
        over,
        rot,
        call,
        equal,
        prep_proc,
        if_start,
        _else,
        end_if,
        end_else,
        end_proc,
    }

    enum IntrinsicType
    {
        plus,
        minus,
        times,
        div,
        cast_bool,
        cast_ptr,
        store32,
        load32,
        fd_write
    }

    [Flags]
    public enum KeywordType
    {
        _int,
        _ptr,
        _bool,
        _if,
        _else,
        end,
        arrow,
        dup,
        drop,
        swap,
        over,
        rot,
        colon,
        equal,
        proc,
        mem,
        _struct,
        wordTypes = proc | mem | _struct,
        dataTypes = _int | _ptr | _bool,
        assignTypes = equal | colon,
    }
}
