namespace Firesharp;

class Types
{
    public record Proc(string name, Contract contract)
    {
        public List<OffsetWord> localMemNames = new();
        public List<TypedWord> localVars = new();
        public List<string> bindings = new();
        public int procMemSize = 0;
        public int bindCount = 0;
    }

    public record struct Contract(List<TokenType> ins, List<TokenType> outs)
    {
        public Contract() : this(new(), new()) {}
    }

    public record struct Loc(string file, int line, int col)
    {
        public static implicit operator Loc((string file, int line, int col) value)
            => new Loc(value.file, value.line, value.col);
        public override string ToString() => $"{file}:{line}:{col}:";
    }

    public record struct IRToken(TokenType type, int operand, Loc loc)
    {
        public IRToken(TypedWord word, Loc loc) : this(word.type, word.value, loc){}
    }
    
    public record Op(OpType type, Loc loc) 
    {
        public int operand = 0;
        public Op(OpType type, int Operand, Loc loc) : this(type, loc) => operand = Operand;
        public static explicit operator Op?(string str) => null;
        public static implicit operator Op((OpType type, Loc loc) value)
            => new Op(value.type, value.loc);
        public static implicit operator Op((OpType type, int operand, Loc loc) value)
            => new Op(value.type, value.operand, value.loc);
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

    public static string TypeNames(TokenType type) => type switch
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
        push_local,
        push_global,
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
        bind_stack,
        push_bind,
        pop_bind,
    }

    public enum IntrinsicType
    {
        plus,
        minus,
        times,
        div,
        cast_bool,
        cast_ptr,
        cast_int,
        store32,
        load32,
        fd_write
    }

    [Flags]
    public enum KeywordType
    {
        _none   = 0,
        _int    = 1 << 0,
        _ptr    = 1 << 1,
        _bool   = 1 << 2,
        _if     = 1 << 3,
        _else   = 1 << 4,
        end     = 1 << 5,
        arrow   = 1 << 6,
        dup     = 1 << 7,
        drop    = 1 << 8,
        swap    = 1 << 9,
        over    = 1 << 10,
        rot     = 1 << 11,
        colon   = 1 << 12,
        equal   = 1 << 13,
        proc    = 1 << 14,
        mem     = 1 << 15,
        _struct = 1 << 16,
        let     = 1 << 17,
        wordTypes = proc | mem | _struct,
        dataTypes = _int | _ptr | _bool,
        assignTypes = equal | colon,
    }

    public record struct OffsetWord(string name, int offset)
    {
        public static implicit operator OffsetWord((string name, int offset) value)
            => new(value.name, value.offset);
    }
    
    public record struct TypedWord(OffsetWord word, TokenType type)
    {
        public TypedWord(string name, int offset, TokenType type) : this ((name, offset), type){}
        public static implicit operator TypedWord((string name, int offset, TokenType type) value)
            => new(value.name, value.offset, value.type);
        public string name => word.name;
        public int value => word.offset;
    }
}
