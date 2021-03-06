namespace Firesharp.Types;

public record Proc(string name, Contract contract)
{
    public List<List<CaseOption>> caseBlocks = new();
    public List<TypedWord> localVars = new();
    public List<Word> localMemNames = new();
    public List<string> bindings = new();
    public int currentBlock = -1;
    public int procMemSize = 0;
    public int bindCount = 0;
}

public record struct CaseOption(CaseType type, int[] value)
{
    public static implicit operator CaseOption((CaseType type, int[] value) value)
        => new CaseOption(value.type, value.value);
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
    public static implicit operator IRToken((TokenType type, int operand, Loc loc) value)
        => new IRToken(value.type, value.operand, value.loc);
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

public record struct Word(string name, int value)
{
    public static implicit operator Word((string name, int value) value)
        => new Word(value.name, value.value);
}

public record SizedWord(Word word)
{
    public int offset = -1;
    public string name => word.name;
    public int size => word.value;
}

public record struct TypedWord(Word word, TokenType type)
{
    public TypedWord(string name, int value, TokenType type) : this ((name, value), type){}
    public static implicit operator TypedWord((string name, int value, TokenType type) value)
        => new TypedWord((value.name, value.value), value.type);
    public string name => word.name;
    public int value => word.value;
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
    case_start,
    case_match,
    case_option,
    end_case,
}

public enum IntrinsicType
{
    plus,
    minus,
    times,
    div,
    greater,
    greater_e,
    lesser,
    lesser_e,
    and,
    or,
    xor,
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
    @if     = 1 << 0,
    @else   = 1 << 1,
    end     = 1 << 2,
    arrow   = 1 << 3,
    dup     = 1 << 4,
    drop    = 1 << 5,
    swap    = 1 << 6,
    over    = 1 << 7,
    rot     = 1 << 8,
    colon   = 1 << 9,
    equal   = 1 << 10,
    proc    = 1 << 11,
    mem     = 1 << 12,
    @struct = 1 << 13,
    let     = 1 << 14,
    @while  = 1 << 15,
    @do     = 1 << 16,
    at      = 1 << 17,
    include = 1 << 18,
    @case   = 1 << 19,
    wordTypes = proc | mem | @struct,
    assignTypes = equal | colon,
}

public enum CaseType
{
    none,
    equal,
    match,
    range,
    lesser,
    lesser_e,
    greater,
    greater_e,
    bit_and,
    @default,
}
