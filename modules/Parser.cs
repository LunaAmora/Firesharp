namespace Firesharp;

using GlobalMem = List<(string name, int size)>;

static partial class Firesharp
{
    static List<string> wordList = new ();
    static Stack<Op> opBlock = new ();
    static GlobalMem memList = new ();

    static void ParseFile(FileStream file, string filepath)
    {
        using (var reader = new StreamReader(file))
        {
            Lexer lexer = new (reader, filepath);
            while(lexer.ParseNextToken() is IRToken token)
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

        bool ReadLine()
        {
            if (stream.ReadLine() is string line)
            {
                lineNum++;
                if(string.IsNullOrWhiteSpace(line)) return(ReadLine());
                
                buffer = line.AsSpan();
                colNum = 0;
                parserPos = 0;
                TrimLeft();
                return true;
            }
            buffer = ReadOnlySpan<char>.Empty;
            return false;
        }

        public void AdvanceByPredicate(Predicate<char> pred)
        {
            while(buffer.Length > parserPos && !pred(buffer[parserPos])) parserPos++;
        }

        public string ReadByPredicate(Predicate<char> pred)
        {
            AdvanceByPredicate(pred);
            return buffer.Slice(colNum, parserPos - colNum).ToString();
        }

        public bool TrimLeft()
        {
            if(buffer.Slice(parserPos).Trim().IsEmpty) return false;
            
            AdvanceByPredicate(pred => pred != ' ');
            colNum = parserPos;
            return true;
        }

        public bool NextToken(out Token token)
        {
            if (!TrimLeft() && !ReadLine())
            {
                token = default;
                return false;
            }
            token = new (ReadByPredicate(pred => pred == ' '), file, lineNum, colNum + 1);
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
        _ when TryParseNumber (tok.name, out int value)
            => new (TokenType._int, value, tok.loc),
        _ when TryParseKeyword (tok.name, out int keyword)
            => new (TokenType._keyword, keyword, tok.loc),
        _ => new (TokenType._word, DefineWord(tok.name), tok.loc)
    };

    static bool TryParseNumber(string word, out int value) => Int32.TryParse(word, out value);

    static bool TryParseKeyword(string word, out int result)
    {
        result = (int) (word switch
        {
            "dup"  => KeywordType.dup,
            "swap" => KeywordType.swap,
            "drop" => KeywordType.drop,
            "over" => KeywordType.over,
            "rot"  => KeywordType.rot,
            "if"   => KeywordType._if,
            "else" => KeywordType._else,
            "end"  => KeywordType.end,
            "memory" => KeywordType.memory,
            _ => (KeywordType)(-1)
        });
        return result >= 0;
    }

    static bool TryGetIntrinsic(int index, out int result)
    {
        result = -1;
        return wordList.Count > index ? TryGetIntrinsic(wordList[index], out result) : false;
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
            "cast(bool)" => IntrinsicType.cast_bool,
            _ => (IntrinsicType)(-1)
        });
        return result >= 0;
    }

    static Op? DefineOp(this IRToken tok, ref Lexer lexer) => tok.Type switch
    {
        TokenType._int     => new (OpType.push_int,  tok.Operand, tok.Loc),
        TokenType._keyword => (KeywordType)tok.Operand switch
        {
            KeywordType.dup    => new (OpType.dup,  tok.Loc),
            KeywordType.swap   => new (OpType.swap, tok.Loc),
            KeywordType.drop   => new (OpType.drop, tok.Loc),
            KeywordType.over   => new (OpType.over, tok.Loc),
            KeywordType.rot    => new (OpType.rot,  tok.Loc),
            KeywordType.memory => lexer.DefineMemory(tok.Loc),
            KeywordType._if    => PushBlock(new (OpType.if_start, tok.Loc)),
            KeywordType._else  => PopBlock(tok.Loc) switch
            {
                {Type: OpType.if_start} => PushBlock(new (OpType._else, tok.Loc)),
                {} op => (Op?) Error(tok.Loc, $"`else` can only come after an `if` block, but found a `{op.Type}` block instead`",
                    $"{op.Loc} [INFO] The found block started here")
            },
            KeywordType.end    => PopBlock(tok.Loc) switch
            {
                {Type: OpType.if_start} => new (OpType.end_if, tok.Loc),
                {Type: OpType._else}    => new (OpType.end_else, tok.Loc),
                {} op => (Op?) Error(tok.Loc, $"`end` can not close a `{op.Type}` block")
            },
            {} typ => (Op?) Error(tok.Loc, $"Keyword type not implemented in `DefineOp` yet: {typ}")
        },
        TokenType._word => tok.Operand switch
        {
            _ when TryGetIntrinsic(tok.Operand, out int result)
                => new (OpType.intrinsic, result, tok.Loc),
            _ => (Op?) Error(tok.Loc, $"Word was not declared on the program: `{wordList[tok.Operand]}`")
        },
        _ => null
    };

    static IRToken? ExpectToken(this ref Lexer lexer, Loc loc, TokenType expectedType, string notFound)
    {
        var sb = new StringBuilder();
        var errorLoc = loc;
        if (lexer.ParseNextToken() is IRToken token)
        {
            if (token.Type.Equals(expectedType)) return token;
            
            sb.Append($"Expected type to be a `{TypeNames(expectedType)}`, but found ");
            errorLoc = token.Loc;
            
            if (token.Type.Equals(TokenType._word) && TryGetIntrinsic(token.Operand, out int intrinsic))
            {
                sb.Append($"the Intrinsic `{(IntrinsicType)intrinsic}`");
            }
            else
            {
                sb.Append($"a `{TypeNames(token.Type)}`");
            }
        }
        else
        {
            sb.Append($"{notFound}, but found nothing");
        }
        Error(errorLoc, sb.ToString());
        return null;
    }

    static IRToken? ExpectKeyword(this ref Lexer lexer, Loc loc, KeywordType expectedType, string notFound)
    {
        var token = lexer.ExpectToken(loc, TokenType._keyword, notFound);
        if(token is IRToken tok && !((KeywordType)tok.Operand).Equals(expectedType))
        {
            Error(tok.Loc, $"Expected keyword to be `{expectedType}`, but found `{(KeywordType)tok.Operand}`");
            return null;
        }
        return token;
    }

    static Op? DefineMemory(this ref Lexer lexer, Loc loc)
    {
        if ((lexer.ExpectToken(loc, TokenType._word, "Expected memory name after `memory`"),
            lexer.ExpectToken(loc, TokenType._int, "Expected memory size after memory name"),
            lexer.ExpectKeyword(loc, KeywordType.end, "Expected `end` after memory size")) is 
            (IRToken nameToken, IRToken valueToken, IRToken endToken))  
        {
            memList.Add((wordList[nameToken.Operand], valueToken.Operand));
        }
        return null;
    }

    static Op PushBlock(Op op)
    {
        opBlock.Push(op);
        return op;
    }

    static Op PopBlock(Loc loc)
    {
        Assert(opBlock.Count > 0, loc, "there are no open blocks to close with `end`");
        return opBlock.Pop();
    }
    
    static Op PeekBlock(Loc loc)
    {
        Assert(opBlock.Count > 0, loc, "there are no open blocks");
        return opBlock.Peek();
    }
}