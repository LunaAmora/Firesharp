namespace Firesharp.Types;

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
    public override string ToString() 
        => string.IsNullOrEmpty(file) ? string.Empty : $"{file}:{line}:{col}: ";
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

public record struct StructType(string name, List<StructMember> members)
{
    public static implicit operator StructType((string name, List<StructMember> members) value)
        => new StructType(value.name, value.members);
    public static implicit operator StructType((string name, TokenType type) value)
        => new StructType(value.name, new(){value.type});
}

public record struct StructMember(string name, TokenType type, int defaultValue = 0)
{
    public static implicit operator StructMember((string name, TokenType type, int defaultValue) value)
        => new StructMember(value.name, value.type, value.defaultValue);
    public static implicit operator StructMember((string name, TokenType type) value)
        => new StructMember(value.name, value.type);
    public static implicit operator StructMember(TokenType type)
        => new StructMember(string.Empty, type);
}

public record struct OffsetWord(string name, int offset)
{
    public static implicit operator OffsetWord((string name, int offset) value)
        => new(value.name, value.offset);
}

public record SizedWord(OffsetWord word)
{
    public int offset = -1;
    public string name => word.name;
    public int    size => word.offset;
}

public record struct TypedWord(OffsetWord word, TokenType type)
{
    public TypedWord(string name, int offset, TokenType type) : this ((name, offset), type){}
    public static implicit operator TypedWord((string name, int offset, TokenType type) value)
        => new(value.name, value.offset, value.type);
    public string name => word.name;
    public int value => word.offset;
}

public record struct TypeFrame(TokenType type, Loc loc)
{
    public static implicit operator TypeFrame((TokenType type, Loc loc) value)
        => new TypeFrame(value.type, value.loc);
    public static implicit operator TypeFrame(IRToken value)
        => new TypeFrame(value.type, value.loc);
}

public enum TokenType
{
    keyword,
    word,
    str,
    @int, // from this index onwards, corresponds to a dataType
    @bool,
    ptr,
    any,
    data_ptr, // from this index onwards, corresponds to an index in the struct list
    // *int
    // *bool
    // *ptr
    // *any      -- Auto added data_ptr types by the compiler
    // *structName...          data_ptr types added by the program
}

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
    offset_load,
    offset,
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
    @else,
    end_if,
    end_else,
    end_proc,
    bind_stack,
    push_bind,
    pop_bind,
    @while,
    @do,
    end_while,
    unpack,
    expectType,
}

public enum IntrinsicType
{
    plus,
    minus,
    times,
    div,
    greater,
    lesser,
    load8,
    store8,
    load16,
    store16,
    load32,
    store32,
    fd_write,
    cast // from this index onwards, corresponds to a dataType starting at `@int`
}

[Flags]
public enum KeywordType
{
    none    = 0,
    @int    = 1 << 0,
    ptr     = 1 << 1,
    @bool   = 1 << 2,
    @if     = 1 << 3,
    @else   = 1 << 4,
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
    @struct = 1 << 16,
    let     = 1 << 17,
    @while  = 1 << 18,
    @do     = 1 << 19,
    at      = 1 << 20,
    include = 1 << 21,
    wordTypes = proc | mem | @struct,
    dataTypes = @int | ptr | @bool,
    assignTypes = equal | colon,
}
