namespace Firesharp;

static partial class Firesharp
{
    static void ParseFile(FileStream file, string filepath)
    {
        using (var reader = new StreamReader(file))
        {
            Lexer lexer = new (reader, filepath);
            while(lexer.ParseNextToken() is IRToken token)
            {
                if (token.DefineOp() is Op op)
                {
                    program.Add(op);
                }
                else
                {
                    Error(token.Loc, $"could not define a op for the token `{token.Type}`");
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

    static IRToken? ParseNextToken(this ref Lexer lexer)
    {
        (Action? error, IRToken? tok) = lexer.TryParseNextToken();
        if (tok is not IRToken && error is Action) error();
        return tok;
    }

    static (Action? error, IRToken? token) TryParseNextToken(this ref Lexer lexer) => lexer.NextToken(out Token tok) switch
    {
        false 
            => (null, null),
        _ when TryParseNumber   (tok.name, out int value)
            => (null, new (DataType._int, value, tok.loc)),
        _ when TryParseKeyword  (tok.name, out KeywordType keyword)
            => (null, new (keyword, tok.loc)),
        _ when TryParseIntrinsic(tok.name, out IntrinsicType intrinsic)
            => (null, new (OpType.intrinsic, (int)intrinsic, tok.loc)),
        _ => (() => Error(tok.loc, $"Could not parse the word `{tok.name}`"), null)
    };

    static bool TryParseNumber(string word, out int value) => Int32.TryParse(word, out value);

    static bool TryParseKeyword(string word, out KeywordType result)
    {
        result = word switch
        {
            "dup"  => KeywordType.dup,
            "swap" => KeywordType.swap,
            "drop" => KeywordType.drop,
            "over" => KeywordType.over,
            "rot"  => KeywordType.rot,
            "if"   => KeywordType._if,
            "else" => KeywordType._else,
            "end"  => KeywordType.end,
            _ => (KeywordType)(-1)
        };
        return result >= 0;
    }

    static bool TryParseIntrinsic(string word, out IntrinsicType result)
    {
        result = word switch
        {
            "+" => IntrinsicType.plus,
            "-" => IntrinsicType.minus,
            "*" => IntrinsicType.times,
            "%" => IntrinsicType.div,
            "=" => IntrinsicType.equal,
            _ => (IntrinsicType)(-1)
        };
        return result >= 0;
    }

    static Op? DefineOp(this IRToken tok) => tok.Type switch
    {
        OpType.intrinsic  => new (OpType.intrinsic, tok.Operand, tok.Loc),
        DataType._int     => new (OpType.push_int,  tok.Operand, tok.Loc),
        KeywordType.dup   => new (OpType.dup,  tok.Loc),
        KeywordType.swap  => new (OpType.swap, tok.Loc),
        KeywordType.drop  => new (OpType.drop, tok.Loc),
        KeywordType.over  => new (OpType.over, tok.Loc),
        KeywordType.rot   => new (OpType.rot,  tok.Loc),
        KeywordType._if   => PushBlock(new (OpType.if_start, tok.Loc)),
        KeywordType._else => PopBlock(tok.Loc) switch
        {
            {Type: OpType.if_start} => PushBlock(new (OpType._else, tok.Loc)),
            _ => null
        },
        KeywordType.end   => PopBlock(tok.Loc) switch
        {
            {Type: OpType.if_start} => new (OpType.end_if, tok.Loc),
            {Type: OpType._else}    => new (OpType.end_else, tok.Loc),
            _ => null
        },
        _ => null
    };

    static Stack<Op> OpBlock = new ();

    static Op PushBlock(Op op)
    {
        OpBlock.Push(op);
        return op;
    }

    static Op PopBlock(Loc loc)
    {
        Assert(OpBlock.Count > 0, loc, "there are no open blocks to close with `end`");
        return OpBlock.Pop();
    }
    
    static Op PeekBlock(Loc loc)
    {
        Assert(OpBlock.Count > 0, loc, "there are no open blocks");
        return OpBlock.Peek();
    }
}