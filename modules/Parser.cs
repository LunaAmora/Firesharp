namespace Firesharp;

static partial class Firesharp
{
    static void ParseFile(FileStream file, string filepath)
    {
        using(var reader = new StreamReader(file))
        {
            var lexer = new Lexer(reader, filepath);
            while(lexer.ParseNextToken());
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

    static bool ParseNextToken(this ref Lexer lexer)
    {
        if (lexer.NextToken(out Token tok))
        {
            if(TryParseNumber(tok.name, out int value))
            {
                DefineOp(DataType._int, value, tok.loc);
            }
            else if(TryParseKeyword(tok.name, out KeywordType keyword))
            {
                DefineOp(keyword, tok.loc);
            }
            else if(TryParseIntrinsic(tok.name, out IntrinsicType intrinsic))
            {
                DefineOp(OpType.intrinsic, (int)intrinsic, tok.loc);
            }
            else Error(tok.loc, $"could not parse the word `{tok.name}`");
            return true;
        }
        return false;
    }

    static bool TryParseNumber(string token, out int value) => Int32.TryParse(token, out value);

    static bool TryParseKeyword(string token, out KeywordType result)
    {
        result = token switch
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

    static bool TryParseIntrinsic(string token, out IntrinsicType result)
    {
        result = token switch
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

    static bool DefineOp<t>(t token, Loc loc) where t : struct, Enum => DefineOp(token, 0, loc);
    static bool DefineOp<t>(t token, int operand, Loc loc)
        where t : struct, Enum => Assert(token switch
    {
        OpType.intrinsic => RegisterOp(OpType.intrinsic, operand, loc),
        DataType._int    => RegisterOp(OpType.push_int,  operand, loc),
        KeywordType.dup  => RegisterOp(OpType.dup,  loc),
        KeywordType.swap => RegisterOp(OpType.swap, loc),
        KeywordType.drop => RegisterOp(OpType.drop, loc),
        KeywordType.over => RegisterOp(OpType.over, loc),
        KeywordType.rot  => RegisterOp(OpType.rot,  loc),
        _ => -1
    } >= 0, loc, $"could not define a op for the token `{token}`");

    static int RegisterOp(OpType type, Loc loc) => RegisterOp(type, 0, loc);
    static int RegisterOp(OpType type, int operand, Loc loc)
    {
        program.Add(new Op(type, operand, loc));
        return program.Count() - 1;
    }
}