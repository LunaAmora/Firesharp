namespace Firesharp;

using DataList = List<(string name, int offset)>;

static partial class Firesharp
{
    static List<string> wordList = new();
    static List<Proc> procList = new();
    static Stack<Op> opBlock = new();
    static DataList memList = new();
    static int totalMemSize = 0;
    static DataList dataList = new();
    static int totalDataSize = 0;
    
    static int finalDataSize => ((totalDataSize + 3)/4)*4;

    static Proc? currentProc;
    static bool insideProc => currentProc != null;

    static void ParseFile(FileStream file, string filepath)
    {
        using (var reader = new StreamReader(file))
        {
            Lexer lexer = new(reader, filepath);
            while (lexer.ParseNextToken() is IRToken token)
            {
                if (token.DefineOp(ref lexer) is Op op)
                {
                    program.Add(op);
                }
            }
        }
    }

    ref struct Lexer
    {
        ReadOnlySpan<char> buffer = ReadOnlySpan<char>.Empty;
        StreamReader stream;
        string file;

        int parserPos = 0;
        int colNum = 0;
        int lineNum = 0;

        public Lexer(StreamReader reader, string filepath)
        {
            stream = reader;
            file = filepath;
        }

        public bool ReadLine()
        {
            if (stream.ReadLine() is string line)
            {
                lineNum++;
                if (string.IsNullOrWhiteSpace(line)) return (ReadLine());

                buffer = line.AsSpan();
                colNum = 0;
                parserPos = 0;
                
                if (!TrimLeft()) return ReadLine();
                return true;
            }
            buffer = ReadOnlySpan<char>.Empty;
            return false;
        }

        public void AdvanceByPredicate(Predicate<char> pred)
        {
            while (buffer.Length > parserPos && !pred(buffer[parserPos])) parserPos++;
        }

        public string ReadByPredicate(Predicate<char> pred)
        {
            AdvanceByPredicate(pred);
            if(colNum == parserPos) parserPos++;
            return buffer.Slice(colNum, parserPos - colNum).ToString();
        }

        public bool TrimLeft()
        {
            if (parserPos > buffer.Length - 1 || buffer.Slice(parserPos).Trim().IsEmpty) return false;

            AdvanceByPredicate(pred => pred != ' ');
            colNum = parserPos;
            if (buffer.Slice(parserPos).StartsWith("//")) return false;

            return true;
        }

        public bool NextToken(out Token token)
        {
            if (!TrimLeft() && !ReadLine())
            {
                token = default;
                return false;
            }
            Predicate<char> pred = (c => c == ' ' || c == ':');
            token = new(ReadByPredicate(pred), new(file, lineNum, colNum + 1));
            return true;
        }
    }

    static int DefineWord(string word)
    {
        wordList.Add(word);
        return wordList.Count - 1;
    }

    static IRToken? ParseNextToken(this ref Lexer lexer) => lexer.NextToken(out Token tok) switch
    {
        false
            => (null),
        _ when TryParseString(ref lexer, tok, out int index)
            => new(TokenType._str, index, tok.loc),
        _ when TryParseNumber(tok.name, out int value)
            => new(TokenType._int, value, tok.loc),
        _ when TryParseKeyword(tok.name, out int keyword)
            => new(TokenType._keyword, keyword, tok.loc),
        _ => new(TokenType._word, DefineWord(tok.name), tok.loc)
    };

    private static bool TryParseString(ref Lexer lexer, Token tok, out int index)
    {
        index = dataList.Count;
        if(tok.name.StartsWith('\"') && tok.name is string name)
        {
            if(!tok.name.EndsWith('\"'))
            {
                lexer.AdvanceByPredicate(pred => pred == '\"');
                name = lexer.ReadByPredicate(pred => pred == ' ');
                Assert(name.EndsWith('\"'), tok.loc, "Missing clossing '\"' in string literal");
            }
             
            name = name.Trim('\"');
            var scapes = name.Count(pred => pred == '\\'); //TODO: This does not take escaped '\' into account
            dataList.Add((name, name.Length - scapes));
            totalDataSize += name.Length;
            return true;
        }
        return false;
    }

    static bool TryParseNumber(string word, out int value) => Int32.TryParse(word, out value);

    static bool TryParseKeyword(string word, out int result)
    {
        result = (int)(word switch
        {
            "dup"  => KeywordType.dup,
            "swap" => KeywordType.swap,
            "drop" => KeywordType.drop,
            "over" => KeywordType.over,
            "rot"  => KeywordType.rot,
            "if"   => KeywordType._if,
            "else" => KeywordType._else,
            "end"  => KeywordType.end,
            "proc" => KeywordType.proc,
            "int"  => KeywordType._int,
            "ptr"  => KeywordType._ptr,
            "bool" => KeywordType._bool,
            "->"   => KeywordType.arrow,
            "mem"  => KeywordType.mem,
            ":"    => KeywordType.colon,
            _ => (KeywordType)(-1)
        });
        return result >= 0;
    }

    static bool TryGetIntrinsic(string word, Loc loc, out Op? result)
    {
        var sucess = TryGetIntrinsic(word, out int res);
        result = new(OpType.intrinsic, res, loc);
        return sucess;
    }

    private static bool TryGetIntrinsic(string word, out int result)
    {
        result = (int)(word switch
        {
            "+" => IntrinsicType.plus,
            "-" => IntrinsicType.minus,
            "*" => IntrinsicType.times,
            "%" => IntrinsicType.div,
            "=" => IntrinsicType.equal,
            "!32" => IntrinsicType.store32,
            "@32" => IntrinsicType.load32,
            "#ptr" => IntrinsicType.cast_ptr,
            "#bool" => IntrinsicType.cast_bool,
            "fd_write" => IntrinsicType.fd_write,
            _ => (IntrinsicType)(-1)
        });
        return result >= 0;
    }

    static Op? DefineOp(this IRToken tok, ref Lexer lexer) => tok.Type switch
    {
        {} typ when ExpectProc(typ, tok.Loc, $"Token type cannot be used outside of a procedure: `{TypeNames(typ)}`") => null,
        TokenType._keyword => DefineOp((KeywordType)tok.Operand, tok.Loc, ref lexer),
        TokenType._int     => new(OpType.push_int, tok.Operand, tok.Loc),
        TokenType._str     => new(OpType.push_str, tok.Operand, tok.Loc),
        TokenType._word    => tok.Operand switch
        {
            _ when (wordList.Count <= tok.Operand) => (Op?)Error(tok.Loc, $"Unreachable"),
            _ when TryGetIntrinsic(wordList[tok.Operand], tok.Loc, out Op? result) => result,
            _ when TryGetLocalMem(wordList[tok.Operand], tok.Loc, out Op? result) => result,
            _ when TryGetGlobalMem(wordList[tok.Operand], tok.Loc, out Op? result) => result,
            _ when TryGetProcName(wordList[tok.Operand], tok.Loc, out Op? result)  => result,
            _ when TryDefineContext(wordList[tok.Operand], tok.Loc, ref lexer, out Op? result) => result,
            _ => (Op?)Error(tok.Loc, $"Word was not declared on the program: `{wordList[tok.Operand]}`")
        },
        _ => (Op?)Error(tok.Loc, $"Token type not implemented in `DefineOp` yet: {tok.Type}")
    };

    static Op? DefineOp(KeywordType type, Loc loc, ref Lexer lexer) => type switch
    {
        KeywordType.dup    => new(OpType.dup, loc),
        KeywordType.swap   => new(OpType.swap, loc),
        KeywordType.drop   => new(OpType.drop, loc),
        KeywordType.over   => new(OpType.over, loc),
        KeywordType.rot    => new(OpType.rot, loc),
        KeywordType._if    => PushBlock(new(OpType.if_start, loc)),
        KeywordType._else  => PopBlock(loc, "there are no open blocks to close with `else`") switch
        {
            {type: OpType.if_start} => PushBlock(new(OpType._else, loc)),
            {} op => (Op?)Error(loc, $"`else` can only come after an `if` block, but found a `{op.type}` block instead`",
                $"{op.loc} [INFO] The found block started here")
        },
        KeywordType.end => PopBlock(loc, "there are no open blocks to close with `end`") switch
        {
            {type: OpType.if_start}  => new(OpType.end_if, loc),
            {type: OpType._else}     => new(OpType.end_else, loc),
            {type: OpType.prep_proc} op => ExitProc(new(OpType.end_proc, op.operand, loc)),
            {} op => (Op?)Error(loc, $"`end` can not close a `{op.type}` block")
        },
        _ => (Op?)Error(loc, $"Keyword type not implemented in `DefineOp` yet: {type}")
    };

    static Op ExitProc(Op op)
    {
        currentProc = null;
        return op;
    }

    static bool TryDefineContext(string word, Loc loc, ref Lexer lexer, out Op? result)
    {
        result = null;
        if (lexer.ParseNextToken() is IRToken tok
            && tok.Type is TokenType._keyword
            && (KeywordType)tok.Operand is KeywordType.colon)
        {
            if (lexer.ParseNextToken() is IRToken tokType && tokType.Type is TokenType._keyword)
            {
                result = (KeywordType)tokType.Operand switch
                {
                    KeywordType.proc => lexer.ParseProcContract(word, new(OpType.prep_proc, loc)),
                    KeywordType.mem  => lexer.DefineMemory(word, loc),
                    {} k => (Op?)Error(loc, $"Keyword not supported in type assignment: {k}")
                };
                return true;
            }
        }
        return false;
    }

    static bool TryGetProcName(string word, Loc loc, out Op? result)
    {
        var index = procList.FindIndex(proc => proc.name.Equals(word));
        result = new(OpType.call, index, loc);
        return index >= 0;
    }

    private static bool TryGetLocalMem(string word, Loc loc, out Op? result)
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

    static Op? ParseProcContract(this ref Lexer lexer, string name, Op op)
    {
        List<TokenType> ins = new();
        List<TokenType> outs = new();
        var foundArrow = false;

        lexer.ExpectKeyword(op.loc, KeywordType.colon, "Expected `:` after keyword `proc`");
        var sb = new StringBuilder("Expected a proc contract or keyword `:` after procedure definition, but found");
        while(lexer.ParseNextToken() is IRToken tok)
        {
            if(tok is {Type: TokenType._keyword} && (KeywordType)tok.Operand is KeywordType typ)
            {
                if (typ switch
                {
                    KeywordType._int  => TokenType._int,
                    KeywordType._ptr  => TokenType._ptr,
                    KeywordType._bool => TokenType._bool,
                    _ => TokenType._keyword
                } is TokenType key && key is not TokenType._keyword)
                {
                    if(!foundArrow) ins.Add(key);
                    else outs.Add(key);
                }
                else if (typ is KeywordType.arrow) foundArrow = true;
                else if (typ is KeywordType.colon)
                {
                    currentProc = new (name, op, new(ins, outs));
                    procList.Add(currentProc);
                    op.operand = procList.Count -1;
                    return PushBlock(op);
                }
                else
                {
                    sb.Append($": `{typ}`");
                    return (Op?)Error(tok.Loc, sb.ToString());
                }
            }
            else
            {
                sb.Append($": `{TypeNames(tok.Type)}`");
                return (Op?)Error(tok.Loc, sb.ToString());
            }
        }
        sb.Append(" nothing");
        return (Op?)Error(op.loc, sb.ToString());
    }

    static IRToken? ExpectToken(this ref Lexer lexer, Loc loc, TokenType expected, string notFound)
    {
        var sb = new StringBuilder();
        var errorLoc = loc;
        if (lexer.ParseNextToken() is IRToken token)
        {
            if (token.Type.Equals(expected)) return token;

            sb.Append($"Expected type to be a `{TypeNames(expected)}`, but found ");
            errorLoc = token.Loc;

            if (token.Type.Equals(TokenType._word) && TryGetIntrinsic(wordList[token.Operand], out int intrinsic))
            {
                sb.Append($"the Intrinsic `{(IntrinsicType)intrinsic}`");
            }
            else sb.Append($"a `{TypeNames(token.Type)}`");
        }
        else sb.Append($"{notFound}, but found nothing");
        Error(errorLoc, sb.ToString());
        return null;
    }

    static IRToken? ExpectKeyword(this ref Lexer lexer, Loc loc, KeywordType expectedType, string notFound)
    {
        var token = lexer.ExpectToken(loc, TokenType._keyword, notFound);
        if (token is IRToken tok && !((KeywordType)tok.Operand).Equals(expectedType))
        {
            Error(tok.Loc, $"Expected keyword to be `{expectedType}`, but found `{(KeywordType)tok.Operand}`");
            return null;
        }
        return token;
    }

    static Op? DefineMemory(this ref Lexer lexer, string word, Loc loc)
    {
        if ((lexer.ExpectKeyword(loc, KeywordType.colon, "Expected `:` after `mem`"),
            lexer.ExpectToken(loc, TokenType._int, "Expected memory size after `:`"),
            lexer.ExpectKeyword(loc, KeywordType.end, "Expected `end` after memory size")) is
            (_, IRToken valueToken, IRToken endToken))
        {
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
        }
        return null;
    }

    static Op? PushBlock(Op? op)
    {
        if(op is Op o) opBlock.Push(o);
        return op;
    }

    static Op PopBlock(Loc loc, string errorText)
    {
        Assert(opBlock.Count > 0, loc, errorText);
        return opBlock.Pop();
    }

    static Op? PeekBlock(Loc loc, string errorText)
    {
        Assert(opBlock.Count > 0, loc, errorText);
        return opBlock.Peek();
    }

    static bool ExpectProc(TokenType type, Loc loc, string errorText)
    {
        return (!(type is TokenType._keyword or TokenType._word || PeekBlock(loc, errorText) is Op));
    }
}