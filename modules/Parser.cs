namespace Firesharp;

using MemList = List<(string name, int offset)>;
using TypeList = List<(string name, int value, TokenType type)>;
using StructVars = List<(string name, int type)>;

static partial class Firesharp
{
    public static List<Op> program = new ();
    static List<StructType> structList = new();
    static List<Proc> procList = new();
    static Stack<Op> opBlock = new();

    static StructVars structVarsList = new();
    static TypeList constList = new();
    static TypeList varList = new();
    
    static MemList memList = new();
    static int totalMemSize = 0;
    
    static Proc? currentProc;
    static bool insideProc => currentProc != null;

    static Queue<IRToken> IRTokens = new();

    public static void ParseFile(FileStream file, string filepath)
    {
        using (var reader = new StreamReader(file))
        {
            Lexer lexer = new(reader, filepath);

            while(lexer.ParseNextToken() is {} token)
            {
                IRTokens.Enqueue(token);
            }
            
            while(NextIRToken() is {} token)
            {
                if(token.DefineOp() is {} op)
                {
                    program.Add(op);
                }
            }
        }
    }
    
    static IRToken IRTokenAt(int i) => IRTokens.ElementAt(i);

    static IRToken? PeekIRToken()
    {
        if (IRTokens.Count > 0) return IRTokens.First();
        return null;
    }

    static IRToken? NextIRToken()
    {
        if (IRTokens.Count > 0) return IRTokens.Dequeue();
        return null;
    }
    
    static bool TryGetIntrinsic(string word, out IntrinsicType result)
    {
        result = (word switch
        {
            "+" => IntrinsicType.plus,
            "-" => IntrinsicType.minus,
            "*" => IntrinsicType.times,
            "%" => IntrinsicType.div,
            "@32" => IntrinsicType.load32,
            "!32" => IntrinsicType.store32,
            "#ptr" => IntrinsicType.cast_ptr,
            "#bool" => IntrinsicType.cast_bool,
            "fd_write" => IntrinsicType.fd_write,
            _ => (IntrinsicType)(-1)
        });
        return result >= 0;
    }

    static Op? DefineOp(this IRToken tok) => tok.Type switch
    {
        {} typ when ExpectProc(typ, tok.Loc, $"Token type cannot be used outside of a procedure: `{TypeNames(typ)}`") => null,
        TokenType._keyword => DefineOp((KeywordType)tok.Operand, tok.Loc),
        TokenType._int     => new(OpType.push_int, tok.Operand, tok.Loc),
        TokenType._str     => new(OpType.push_str, tok.Operand, tok.Loc),
        TokenType._word    => wordList[tok.Operand] switch
        {
            var word when TryGetIntrinsic(word, tok.Loc) is {} result => result,
            var word when TryGetLocalMem(word, tok.Loc)  is {} result => result,
            var word when TryGetGlobalMem(word, tok.Loc) is {} result => result,
            var word when TryGetProcName(word, tok.Loc)  is {} result => result,
            var word when TryGetConstName(word, tok.Loc) is {} result => result,
            var word when TryGetLocalVar(word, tok.Loc, out Op? result) => result,
            var word when TryGetGlobalVar(word, tok.Loc, out Op? result) => result,
            var word when TryDefineContext(word, tok.Loc, out Op? result) => result,
            var word => (Op?)Error(tok.Loc, $"Word was not declared on the program: `{word}`")
        },
        _ => (Op?)Error(tok.Loc, $"Token type not implemented in `DefineOp` yet: {tok.Type}")
    };

    static Op? DefineOp(KeywordType type, Loc loc) => type switch
    {
        KeywordType.dup   => new(OpType.dup,  loc),
        KeywordType.swap  => new(OpType.swap, loc),
        KeywordType.drop  => new(OpType.drop, loc),
        KeywordType.over  => new(OpType.over, loc),
        KeywordType.rot   => new(OpType.rot,  loc),
        KeywordType.equal => new(OpType.equal,loc),
        KeywordType._if   => PushBlock(new(OpType.if_start, loc)),
        KeywordType._else => PopBlock(loc, type) switch
        {
            {type: OpType.if_start} => PushBlock(new(OpType._else, loc)),
            {} op => (Op?)Error(loc, $"`else` can only come after an `if` block, but found a `{op.type}` block instead`",
                $"{op.loc} [INFO] The found block started here")
        },
        KeywordType.end => PopBlock(loc, type) switch
        {
            {type: OpType.if_start} => new(OpType.end_if, loc),
            {type: OpType._else}    => new(OpType.end_else, loc),
            {type: OpType.prep_proc} op => ExitProc(new(OpType.end_proc, op.operand, loc)),
            {} op => (Op?)Error(loc, $"`end` can not close a `{op.type}` block")
        },
        _ => (Op?)Error(loc, $"Keyword type not implemented in `DefineOp` yet: {type}")
    };

    static bool ExpectProc(TokenType type, Loc loc, string errorText)
    {
        return !Assert(type is TokenType._keyword or TokenType._word || insideProc, loc, errorText);
    }

    static Op ExitProc(Op op)
    {
        currentProc = null;
        return op;
    }

    static Op? TryGetIntrinsic(string word, Loc loc)
    {
        if(TryGetIntrinsic(word, out IntrinsicType res))
            return new(OpType.intrinsic, (int)res, loc);
        return null;
    }

    static Op? TryGetProcName(string word, Loc loc)
    {
        var index = procList.FindIndex(proc => proc.name.Equals(word));
        return index >= 0 ? new(OpType.call, index, loc) : null;
    }

    static Op? TryGetConstName(string word, Loc loc)
    {
        var index = constList.FindIndex(cnst => cnst.name.Equals(word));
        if(index >= 0)
        {
            var cnst = constList[index];
            return DefineOp(new(cnst.type, cnst.value, loc));
        }
        return null;
    }

    static Op? TryGetLocalMem(string word, Loc loc)
    {
        if(currentProc is Proc proc)
        {
            var index = proc.localMemNames.FindIndex(mem => mem.name.Equals(word));
            if (index != - 1)
            {
                return new (OpType.push_local_mem, proc.localMemNames[index].offset, loc);
            }
        }
        return null;
    }

    static bool TryGetLocalVar(string word, Loc loc, out Op? result)
    {
        result = null;
        if(currentProc is not {} proc) return false;
        
        var store = false;
        if (word.StartsWith('!'))
        {
            word = word.Split('!')[1];
            store = true;
        }

        var index = proc.localVars.FindIndex(val => val.name.Equals(word));
        if (index >= 0)
        {
            if (store) result = new(OpType.store_local, index, loc);
            else       result = new(OpType.load_local, index, loc);
            return true;
        }
        
        index = proc.localVars.FindIndex(val => val.name.StartsWith($"{word}."));
        if (index >= 0 && TryGetStructVars(word) is {} structType)
        {
            var members = new List<StructMember>(structType.members);
            if(store) members.Reverse();

            foreach (var member in members)
            {
                index = proc.localVars.FindIndex(val => $"{word}.{member.name}".Equals(val.name));
                if (store) program.Add(new(OpType.store_local, index, loc));
                else       program.Add(new(OpType.load_local, index, loc));
            }
            return true;
        }
        return false;
    }

    static bool TryGetGlobalVar(string word, Loc loc, out Op? result)
    {
        var store = false;
        if (word.StartsWith('!'))
        {
            word = word.Split('!')[1];
            store = true;
        }

        var index = varList.FindIndex(val => word.Equals(val.name));
        if (index >= 0)
        {
            if (store) result = new(OpType.store_global, index, loc);
            else       result = new(OpType.load_global, index, loc);
            return true;
        }
        else if(TryGetStructVars(word) is {} structType)
        {
            List<StructMember> members = new (structType.members);
            if(store) members.Reverse();

            foreach (var member in members)
            {
                index = varList.FindIndex(val => $"{word}.{member.name}".Equals(val.name));
                if (store) program.Add(new(OpType.store_global, index, loc));
                else       program.Add(new(OpType.load_global, index, loc));
            }
            result = null;
            return true;
        }
        result = null;
        return false;
    }

    static StructType? TryGetStructVars(string word)
    {
        var index = structVarsList.FindIndex(vars => vars.name.Equals(word));
        if (index != - 1)
        {
            return TryGetTypeName(wordList[structVarsList[index].type]);
        }
        return null;
    }

    static StructType? TryGetTypeName(string word)
    {
        var index = structList.FindIndex(type => type.name.Equals(word));
        if (index != - 1) return structList[index];
        return null;
    }
    
    static Op? TryGetGlobalMem(string word, Loc loc)
    {
        var index = memList.FindIndex(mem => mem.name.Equals(word));
        if (index != - 1)
        {
            return new(OpType.push_global_mem, memList[index].offset, loc);
        }
        return null;
    }

    static bool TryDefineContext(string word, Loc loc, out Op? result)
    {
        result = null;
        var colonCount = 0;
        KeywordType? context = null;
        for (int i = 0; i < IRTokens.Count; i++)
        {
            var token = IRTokenAt(i);
            if (token.Type is not TokenType._keyword)
            {
                if(colonCount == 0)
                {
                    if(token.Type is TokenType._word
                        && TryGetTypeName(wordList[token.Operand]) is {} structType)
                    {
                        NextIRToken();
                        ParseStructVar(word, loc, token.Operand, structType);
                        return true;
                    }
                    return false;
                }
                else if(colonCount == 1 && token.Type is TokenType._int)
                {
                    var lastToken = IRTokenAt(i-1);
                    if(lastToken.Type is TokenType._keyword 
                        && (KeywordType)lastToken.Operand is KeywordType.equal)
                    {
                        context = KeywordType._int;
                    }
                    else context = KeywordType.mem;
                    break;
                }
                else if(colonCount == 1 && token.Type is TokenType._word)
                {
                    if (TryGetTypeName(wordList[token.Operand]) is {} structType)
                    {
                        context = KeywordType.proc;
                        break;
                    }
                    else if(IRTokens.Count > i+2)
                    {
                        var n1 = IRTokenAt(i+1);
                        var n2 = IRTokenAt(i+2);
                        if(n1 is {Type: TokenType._keyword})
                        {
                            if(KeywordToDataType((KeywordType)n1.Operand) is not TokenType._keyword && (
                                (n2 is {Type: TokenType._keyword} && (KeywordType)n2.Operand is KeywordType.end) ||
                                (n2 is {Type: TokenType._word})))
                            {
                                context = KeywordType._struct;
                                break;
                            }
                        }
                    }
                }
                
                var invalidToken = token.Type is TokenType._word ? 
                    wordList[token.Operand] : token.Type.ToString();
                Error(token.Loc, $"Invalid Token found on context declaration: `{invalidToken}`");
                return false;
            }
            
            context = (KeywordType)token.Operand;

            if(colonCount == 0 && (KeywordType.wordTypes | KeywordType.dataTypes).HasFlag(context))
            {
                NextIRToken();
                break;
            }
            else if(context is KeywordType.colon) colonCount++;
            else if(context is KeywordType.end)
            {
                Error(token.Loc, $"Missing body or contract necessary to infer the type of the word: `{word}`");
                return false;
            }

            if(colonCount == 2)
            {
                if(i == 1)
                {
                    Warn(loc, "Ambiguous type inference between `const` and `proc`, consider declaring the type for now.");
                    result = DefineProc(word, new(OpType.prep_proc, loc), new ());
                    NextIRToken();
                    NextIRToken();
                    return true;
                }
                
                context = KeywordType.proc;
                break;
            }
        }

        return context switch
        {
            KeywordType.proc => ParseProcedure(word, ref result, new(OpType.prep_proc, loc)),
            KeywordType.mem  => ParseMemory(word, loc),
            KeywordType._struct => ParseStruct(word, loc), 
            _ when KeywordToDataType(context) is {} tokType
              => ParseConstOrVar(word, loc, tokType, ref result),
            _ => false
        };
    }

    static void ParseStructVar(string word, Loc loc, int wordIndex, StructType structType)
    {
        ExpectKeyword(loc, KeywordType.colon, "`:` after variable type definition");
        ExpectKeyword(loc, KeywordType.equal, "`=` after keyword `:`");
        ExpectKeyword(loc, KeywordType.end, "`end` after variable declaration");
        foreach (var member in structType.members)
        {
            var structVar = ($"{word}.{member.name}", member.defaultValue, member.type);
            if (currentProc is {} proc) currentProc.localVars.Add(structVar);
            else varList.Add(structVar);
        }
        structVarsList.Add((word, wordIndex));
    }

    static bool ParseStruct(string word, Loc loc)
    {
        var members = new List<StructMember>();
        ExpectKeyword(loc, KeywordType.colon, "`:` after keyword `struct`");
        while(PeekIRToken() is {} token)
        {
            if(token.Type is TokenType._keyword)
            {
                ExpectKeyword(loc, KeywordType.end, "`end` after struct declaration");
                structList.Add(new (word, members));
                return true;
            }

            var name = ExpectToken(loc, TokenType._word, "struct member name");
            var type = ExpectKeyword(loc, KeywordType.dataTypes, "struct member type");

            if(name is {Operand: int index} && type is {Operand: int keyType}
                && KeywordToDataType((KeywordType)keyType) is {} tokType)
            {
                members.Add(new (wordList[index], tokType));
            }
        }
        Error(loc, "Expected struct members or `end` after struct declaration");
        return false;
    }

    static bool ParseConstOrVar(string word, Loc loc, TokenType tokType, ref Op? result)
    {
        ExpectKeyword(loc, KeywordType.colon, $"`:` after `{tokType}`");
        var assignType = ExpectKeyword(loc, KeywordType.assignTypes, $"`:` or `=` after `{TypeNames(tokType)}`");

        if (assignType is {Operand: int op} && (KeywordType)op is {} keyword)
        {
            var value = 0;
            if (PeekIRToken() is {} valueToken && valueToken.Type == tokType)
            {
                value = valueToken.Operand;
                NextIRToken();
            }
            ExpectKeyword(loc, KeywordType.end, $"`end` after `{TypeNames(tokType)}` declaration");

            if (keyword is KeywordType.equal)
            {
                var newVar = (word, value, tokType);
                if (currentProc is {} proc) currentProc.localVars.Add(newVar);
                else varList.Add(newVar);
            }
            else if (keyword is KeywordType.colon)
            {
                constList.Add((word, value, tokType));
            }
            return true;
        }
        return false;
    }

    static bool ParseProcedure(string name, ref Op? result, Op op)
    {
        List<TokenType> ins = new();
        List<TokenType> outs = new();
        var foundArrow = false;

        ExpectKeyword(op.loc, KeywordType.colon, "Expected `:` after keyword `proc`");
        var sb = new StringBuilder("Expected a proc contract or keyword `:` after procedure definition, but found");
        while(NextIRToken() is {} tok)
        {
            if(tok is {Type: TokenType._keyword} && (KeywordType)tok.Operand is {} typ)
            {
                if (KeywordToDataType(typ) is {} key && key is not TokenType._keyword)
                {
                    if(!foundArrow) ins.Add(key);
                    else outs.Add(key);
                }
                else if (typ is KeywordType.arrow)
                {
                    if(!foundArrow) foundArrow = true;
                    else return Assert(false, tok.Loc, "Duplicated `->` found on procedure definition");
                }
                else if (typ is KeywordType.colon)
                {
                    result = DefineProc(name, op, new(ins, outs));
                    return true;
                }
                else
                {
                    sb.Append($": `{typ}`");
                    return Assert(false, tok.Loc, sb.ToString());
                }
            }
            else if (tok is {Type: TokenType._word} && TryGetTypeName(wordList[tok.Operand]) is {} structType)
            {
                foreach (var member in structType.members)
                {
                    if(!foundArrow) ins.Add(member.type);
                    else outs.Add(member.type);
                }
            }
            else
            {
                sb.Append($": `{TypeNames(tok.Type)}`");
                return Assert(false, tok.Loc, sb.ToString());
            }
        }
        sb.Append(" nothing");
        return Assert(false, op.loc, sb.ToString());
    }

    static TokenType KeywordToDataType(KeywordType? type) => type switch
    {
        KeywordType._int => TokenType._int,
        KeywordType._ptr => TokenType._ptr,
        KeywordType._bool => TokenType._bool,
        _ => TokenType._keyword
    };

    static Op? DefineProc(string name, Op op, Contract contract)
    {
        Assert(!insideProc, op.loc, "Cannot define a procedure inside of another procedure");
        currentProc = new(name, contract);
        procList.Add(currentProc);
        op.operand = procList.Count - 1;
        
        return PushBlock(op);
    }

    static IRToken? ExpectToken(Loc loc, TokenType expected, string notFound)
    {
        var sb = new StringBuilder();
        var errorLoc = loc;
        if (NextIRToken() is {} token)
        {
            if (token.Type.Equals(expected)) return token;

            sb.Append($"Expected {TypeNames(expected)} {notFound}, but found ");
            errorLoc = token.Loc;

            if (token.Type.Equals(TokenType._word) && TryGetIntrinsic(wordList[token.Operand], out IntrinsicType intrinsic))
            {
                sb.Append($"the Intrinsic `{intrinsic}`");
            }
            else sb.Append($"a `{TypeNames(token.Type)}`");
        }
        else sb.Append($"Expected {notFound}, but found nothing");
        Error(errorLoc, sb.ToString());
        return null;
    }

    static IRToken? ExpectKeyword(Loc loc, KeywordType expectedType, string notFound)
    {
        var token = ExpectToken(loc, TokenType._keyword, notFound);
        if (token is {} tok && !(expectedType.HasFlag((KeywordType)tok.Operand)))
        {
            Error(tok.Loc, $"Expected keyword to be `{expectedType}`, but found `{(KeywordType)tok.Operand}`");
            return null;
        }
        return token;
    }

    static bool ParseMemory(string word, Loc loc)
    {
        ExpectKeyword(loc, KeywordType.colon, "`:` after `mem`");
        if (ExpectToken(loc, TokenType._int, "memory size after `:`") is {} valueToken)
        {
            ExpectKeyword(loc, KeywordType.end, "`end` after memory size");
            var size = ((valueToken.Operand + 3)/4)*4;
            if (currentProc is {} proc)
            {
                proc.localMemNames.Add((word, proc.procMemSize));
                proc.procMemSize += size;
            }
            else
            {
                memList.Add((word, totalMemSize));
                totalMemSize += size;
            }
            return true;
        }
        return false;
    }

    static Op? PushBlock(Op? op)
    {
        if(op is {} o) opBlock.Push(o);
        return op;
    }

    static Op PopBlock(Loc loc, KeywordType closingType)
    {
        Assert(opBlock.Count > 0, loc, $"There are no open blocks to close with `{closingType}`");
        return opBlock.Pop();
    }

    public struct StructType
    {
        public string name;
        public List<StructMember> members;

        public StructType(string Name, List<StructMember> Members)
        {
            name = Name;
            members = Members;
        }
    }

    public struct StructMember
    {
        public string name;
        public TokenType type;
        public int defaultValue = 0;

        public StructMember(string Name, TokenType Keyword)
        {
            name = Name;
            type = Keyword;
        }

        public StructMember(String Name, TokenType Keyword, int DefaultValue) : this(Name, Keyword)
        {
            defaultValue = DefaultValue;
        }
    }
}
