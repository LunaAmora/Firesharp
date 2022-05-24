namespace Firesharp;

static partial class Firesharp
{
    static void ParseFile(FileStream file, string filepath)
    {
        using(var reader = new StreamReader(file))
        {
            var lexer = new Lexer(reader, filepath);
            while(lexer.ParseNextToken() is IRToken token)
            {
                token.DefineOp();
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
                buffer = line.AsSpan();
                colNum = 0;
                parserPos = 0;
                lineNum++;
                return true;
            }
            buffer = ReadOnlySpan<char>.Empty;
            return false;
        }

        public void AdvanceByPredicate(Predicate<char> pred)
        {
            while(buffer.Length > parserPos && !pred(buffer[parserPos]))
            {
                parserPos++;
            }
        }

        public string ReadByPredicate(Predicate<char> pred)
        {
            AdvanceByPredicate(pred);
            return buffer.Slice(colNum, parserPos - colNum).ToString();
        }

        public void TrimLeft()
        {
            AdvanceByPredicate(pred => pred != ' ');
            colNum = parserPos;
        }

        public bool NextToken(out Token token)
        {
            if ((buffer.IsEmpty || parserPos >= buffer.Length - 1) && !ReadLine())
            {
                token = default;
                return false;
            }

            TrimLeft();
            token = new Token(ReadByPredicate(pred => pred == ' '), file, lineNum, colNum + 1);
            return true;
        }
    }

    static IRToken? ParseNextToken(this ref Lexer lexer)
    {
        IRToken? nextToken = null;
        if (lexer.NextToken(out Token tok))
        {
            if(TryParseNumber(tok.name, out int value))
            {
                nextToken = new IRToken(DataType._int, value, tok.loc);
            }
            else if(TryParseKeyword(tok.name, out KeywordType keyword))
            {
                nextToken = new IRToken(keyword, tok.loc);
            }
            else if(TryParseIntrinsic(tok.name, out IntrinsicType intrinsic))
            {
                nextToken = new IRToken(OpType.intrinsic, (int)intrinsic, tok.loc);
            }
            else Error(tok.loc, $"could not parse the word `{tok.name}`");
        }
        return nextToken;
    }

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
            _ => (KeywordType)(-1)
        };
        return (int)result >= 0;
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
        return (int)result >= 0;
    }

    static bool DefineOp(this IRToken tok) => Assert(tok.Type switch
    {
        OpType.intrinsic => RegisterOp(OpType.intrinsic, tok.Operand, tok.Loc),
        DataType._int    => RegisterOp(OpType.push_int,  tok.Operand, tok.Loc),
        KeywordType.dup  => RegisterOp(OpType.dup,  tok.Loc),
        KeywordType.swap => RegisterOp(OpType.swap, tok.Loc),
        KeywordType.drop => RegisterOp(OpType.drop, tok.Loc),
        KeywordType.over => RegisterOp(OpType.over, tok.Loc),
        KeywordType.rot  => RegisterOp(OpType.rot,  tok.Loc),
        _ => -1
    } >= 0, tok.Loc, $"could not define a op for the token `{tok.Type}`");

    static int RegisterOp(OpType type, Loc loc) => RegisterOp(type, 0, loc);
    static int RegisterOp(OpType type, int operand, Loc loc)
    {
        program.Add(new Op(type, operand, loc));
        return program.Count() - 1;
    }
}