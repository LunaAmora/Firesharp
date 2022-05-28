namespace Firesharp;

using MemList = List<(string name, int size)>;

static partial class Firesharp
{
    static List<string> wordList = new ();
    static Stack<Op> opBlock = new ();
    static MemList memList = new ();

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
        _ when TryParseNumber   (tok.name, out int value)
            => new (TokenType._int, value, tok.loc),
        _ when TryParseKeyword  (tok.name, out int keyword)
            => new (TokenType._keyword, keyword, tok.loc),
        _ when TryParseIntrinsic(tok.name, out int intrinsic)
            => new (OpType.intrinsic, intrinsic, tok.loc),
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

    static bool TryParseIntrinsic(string word, out int result)
    {
        result = (int)(word switch
        {
            "+" => IntrinsicType.plus,
            "-" => IntrinsicType.minus,
            "*" => IntrinsicType.times,
            "%" => IntrinsicType.div,
            "=" => IntrinsicType.equal,
            _ => (IntrinsicType)(-1)
        });
        return result >= 0;
    }

    static Op? DefineOp(this IRToken tok, ref Lexer lexer) => tok.Type switch
    {
        OpType.intrinsic   => new (OpType.intrinsic, tok.Operand, tok.Loc),
        TokenType._int     => new (OpType.push_int,  tok.Operand, tok.Loc),
        TokenType._keyword => (KeywordType)tok.Operand switch
        {
            KeywordType.dup    => new (OpType.dup,  tok.Loc),
            KeywordType.swap   => new (OpType.swap, tok.Loc),
            KeywordType.drop   => new (OpType.drop, tok.Loc),
            KeywordType.over   => new (OpType.over, tok.Loc),
            KeywordType.rot    => new (OpType.rot,  tok.Loc),
            KeywordType.memory => lexer.DefineMem(tok.Loc),
            KeywordType._if    => PushBlock(new (OpType.if_start, tok.Loc)),
            KeywordType._else  => PopBlock(tok.Loc) switch
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
        },
        _ => null
    };

    static Op? DefineMem(this ref Lexer lexer, Loc loc)
    {
        if (lexer.ParseNextToken() is not IRToken nameToken)
        {
            Error(loc, "Expected memory name after `memory`, but found nothing");
            return null;
        }
        
        if(nameToken.Type is not TokenType._word)
        {
            Error(loc, $"Expected token type to be a `Word`, but found a `{TokenTypeName((TokenType)nameToken.Type)}`");
            return null;
        }

        if (lexer.ParseNextToken() is not IRToken valueToken)
        {
            Error(loc, "Expected memory size after memory name, but found nothing");
            return null;
        }
        
        if(valueToken.Type is not TokenType._int)
        {
            Error(loc, $"Expected token type to be a `Integer`, but found a `{TokenTypeName((TokenType)valueToken.Type)}`");
            return null;
        }

        if (lexer.ParseNextToken() is not IRToken endToken)
        {
            Error(loc, "Expected `end` after memory size, but found nothing");
            return null;
        }

        if(!(endToken.Type is TokenType._keyword && (KeywordType)endToken.Operand is KeywordType.end))
        {
            Error(loc, $"Expected `end` to close the memory definition, but found a `{TokenTypeName((TokenType)valueToken.Type)}`");
            return null;
        }

        memList.Add((wordList[nameToken.Operand], valueToken.Operand));
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