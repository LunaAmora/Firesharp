namespace Firesharp;

using MemList = List<(string name, int offset)>;
using TypeList = List<(string name, int value, TokenType type)>;

static partial class Firesharp
{
    static List<Proc> procList = new();
    static Stack<Op> opBlock = new();

    static TypeList constList = new();
    static TypeList varList = new();
    
    static MemList memList = new();
    static int totalMemSize = 0;
    
    static Proc? currentProc;
    static bool insideProc => currentProc != null;

    static Queue<IRToken> IRTokens = new();

    static void ParseFile(FileStream file, string filepath)
    {
        using (var reader = new StreamReader(file))
        {
            Lexer lexer = new(reader, filepath);

            while(lexer.ParseNextToken() is IRToken token)
            {
                IRTokens.Enqueue(token);
            }
            
            while(NextIRToken() is IRToken token)
            {
                if(token.DefineOp() is Op op)
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
            {} word when TryGetIntrinsic(word, tok.Loc, out Op? result)  => result,
            {} word when TryGetLocalMem(word, tok.Loc, out Op? result)   => result,
            {} word when TryGetGlobalMem(word, tok.Loc, out Op? result)  => result,
            {} word when TryGetGlobalVar(word, tok.Loc, out Op? result)  => result,
            {} word when TryGetProcName(word, tok.Loc, out Op? result)   => result,
            {} word when TryGetConstName(word, tok.Loc, out Op? result)  => result,
            {} word when TryDefineContext(word, tok.Loc, out Op? result) => result,
            {} word => (Op?)Error(tok.Loc, $"Word was not declared on the program: `{word}`")
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

    static bool TryGetIntrinsic(string word, Loc loc, out Op? result)
    {
        var success = TryGetIntrinsic(word, out IntrinsicType res);
        result = new(OpType.intrinsic, (int)res, loc);
        return success;
    }

    static bool TryGetProcName(string word, Loc loc, out Op? result)
    {
        var index = procList.FindIndex(proc => proc.name.Equals(word));
        result = new(OpType.call, index, loc);
        return index >= 0;
    }

    static bool TryGetConstName(string word, Loc loc, out Op? result)
    {
        var index = constList.FindIndex(cnst => cnst.name.Equals(word));
        var found = index >= 0;
        if(found)
        {
            var cnst = constList[index];
            result = DefineOp(new(cnst.type, cnst.value, loc));
        }
        else result = null;
        return found;
    }

    static bool TryGetLocalMem(string word, Loc loc, out Op? result)
    {
        if(currentProc is Proc proc)
        {
            var index = proc.localMemNames.FindIndex(mem => mem.name.Equals(word));
            if (index != - 1)
            {
                result = new (OpType.push_local_mem, proc.localMemNames[index].offset, loc);
                return true;
            }
        }
        result = null;
        return false;
    }

    static bool TryGetGlobalVar(string word, Loc loc, out Op? result)
    {
        var index = varList.FindIndex(mem => word.EndsWith(mem.name) && word.Length - mem.name.Length <= 1);
        if (index != - 1)
        {
            var global = varList[index];
            if(word.Equals(global.name))
            {
                result = new(OpType.load_var, index, loc);
                return true;
            }
            else if (word.StartsWith('!'))
            {
                result = new(OpType.store_var, index, loc);
                return true;
            }
        }
        result = null;
        return false;
    }

    static bool TryGetGlobalMem(string word, Loc loc, out Op? result)
    {
        var index = memList.FindIndex(mem => mem.name.Equals(word));
        if (index != - 1)
        {
            result = new (OpType.push_global_mem, memList[index].offset, loc);
            return true;
        }
        result = null;
        return false;
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
                if(colonCount == 0) return false;
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
                    Error("not implemented type inference for structs yet");
                }
                else
                {
                    var invalidToken = token.Type is TokenType._word ? 
                        wordList[token.Operand] : token.Type.ToString();
                    Error(token.Loc, $"Invalid Token found on context declaration: `{invalidToken}`");
                    return false;
                }
            }
            
            context = (KeywordType)token.Operand;

            if(colonCount == 0 && (context is 
                KeywordType.proc or 
                KeywordType.mem  or
                KeywordType._struct || 
                KeywordToType(context, out TokenType _)))
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
                    Info(loc, "Ambiguous type inference bettwen `const` and `proc`, consider declaring the type for now.");
                    result = DefineProc(word, new(OpType.prep_proc, loc), null);
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
            _ when KeywordToType(context, out TokenType tokType)
              => ParseConstOrVar(word, loc, tokType, ref result),
            _ => false
        };
    }

    private static bool ParseStruct(string word, Loc loc)
    {
        Error(loc, "Struct parsing is not implemented yet");
        return false;
    }

    static bool ParseConstOrVar(string word, Loc loc, TokenType tokType, ref Op? result)
    {
        ExpectKeyword(loc, KeywordType.colon, $"`:` after `{tokType}`");
        var irt = ExpectKeyword(loc, KeywordType.colon | KeywordType.equal, $"`:` or `=` after `{TypeNames(tokType)}`");

        if (irt is IRToken ir && (KeywordType)ir.Operand is KeywordType keyword)
        {
            var value = 0;
            if (PeekIRToken() is IRToken valueToken && valueToken.Type ==  tokType)
            {
                value = valueToken.Operand;
                NextIRToken();
            }
            ExpectKeyword(loc, KeywordType.end, $"`end` after `{TypeNames(tokType)}` declaration");

            if (keyword is KeywordType.equal)
            {
                varList.Add((word, value, tokType));
                result = new(OpType.global_var, varList.Count - 1, loc);
            }
            else if (keyword is KeywordType.colon)
            {
                constList.Add((word, value, tokType));
            }
            return true;
        }
        else return false;
    }

    static bool ParseProcedure(string name, ref Op? result, Op op)
    {
        List<TokenType> ins = new();
        List<TokenType> outs = new();
        var foundArrow = false;

        ExpectKeyword(op.loc, KeywordType.colon, "Expected `:` after keyword `proc`");
        var sb = new StringBuilder("Expected a proc contract or keyword `:` after procedure definition, but found");
        while(NextIRToken() is IRToken tok)
        {
            if(tok is {Type: TokenType._keyword} && (KeywordType)tok.Operand is KeywordType typ)
            {
                if (KeywordToType(typ, out TokenType  key) && key is not TokenType._keyword)
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
            else
            {
                sb.Append($": `{TypeNames(tok.Type)}`");
                return Assert(false, tok.Loc, sb.ToString());
            }
        }
        sb.Append(" nothing");
        return Assert(false, op.loc, sb.ToString());
    }

    static bool KeywordToType(KeywordType? type, out TokenType typ)
    {
        typ = type switch
        {
            KeywordType._int  => TokenType._int,
            KeywordType._ptr  => TokenType._ptr,
            KeywordType._bool => TokenType._bool,
            _ => TokenType._keyword
        };
        return (typ is not TokenType._keyword);
    }

    static Op? DefineProc(string name, Op op, Contract? contract)
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
        if (NextIRToken() is IRToken token)
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
        if (token is IRToken tok && !(expectedType.HasFlag((KeywordType)tok.Operand)))
        {
            Error(tok.Loc, $"Expected keyword to be `{expectedType}`, but found `{(KeywordType)tok.Operand}`");
            return null;
        }
        return token;
    }

    static bool ParseMemory(string word, Loc loc)
    {
        ExpectKeyword(loc, KeywordType.colon, "`:` after `mem`");
        if (ExpectToken(loc, TokenType._int, "memory size after `:`") is IRToken valueToken)
        {
            ExpectKeyword(loc, KeywordType.end, "`end` after memory size");
            var size = ((valueToken.Operand + 3)/4)*4;
            if (currentProc is Proc proc)
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
        if(op is Op o) opBlock.Push(o);
        return op;
    }

    static Op PopBlock(Loc loc, KeywordType closingType)
    {
        Assert(opBlock.Count > 0, loc, $"There are no open blocks to close with `{closingType}`");
        return opBlock.Pop();
    }
}
