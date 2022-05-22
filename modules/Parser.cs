namespace Firesharp;

static partial class Firesharp
{
    static void ParseFile(FileStream file)
    {
        using(var reader = new StreamReader(file))
        {
            var parser = new Parser(reader);
            ParseTokens(parser);
        }
    }

    ref struct Parser
    {
        StreamReader stream;
        ReadOnlySpan<char> buffer = ReadOnlySpan<char>.Empty;
        
        int parserPos = 0;
        public int colNum = 0;
        public int lineNum = 0;

        public Parser(StreamReader reader) => stream = reader;

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
            AdvanceByPredicate((pred) => pred != ' ');
            colNum = parserPos;
        }

        public bool NextToken(out string token)
        {
            if(buffer.IsEmpty || parserPos >= buffer.Length - 1)
            {
                if (!ReadLine())
                {
                    token = "";
                    return false;
                }
            }
            
            TrimLeft();
            token = ReadByPredicate((pred) => pred == ' ');
            return true;
        }
    }

    static void ParseTokens(Parser parser)
    {
        while (parser.NextToken(out string token))
        {
            Loc loc = new Loc(parser.lineNum, parser.colNum + 1);
            if(TryParseNumber(token, out int value))
            {
                program.Add(new Op(OpType.push_int, value, loc));
            }
            else if(TryParseKeyword(token, out KeywordType keyword))
            {
                TryLexToken(keyword, loc)();
            }
            else if(TryParseIntrinsic(token, out IntrinsicType intrinsic))
            {
                program.Add(new Op(OpType.intrinsic, (int)intrinsic, loc));
            }
            else Exit($"could not parse the word `{token}` at line {loc.line}, col {loc.pos}");
        }
    }

    static Action TryLexToken<t>(t token, Loc loc)
        where t : struct, Enum => token switch
    {
        KeywordType.dup  => () => program.Add(new Op(OpType.dup,  loc)),
        KeywordType.swap => () => program.Add(new Op(OpType.swap, loc)),
        KeywordType.drop => () => program.Add(new Op(OpType.drop, loc)),
        KeywordType.over => () => program.Add(new Op(OpType.over, loc)),
        KeywordType.rot  => () => program.Add(new Op(OpType.rot,  loc)),
        _ => () => Exit($"could not lex the token `{token}`")
    };

    static bool TryParseKeyword(string str, out KeywordType result)
    {
        result = str switch
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

    static bool TryParseIntrinsic(string str, out IntrinsicType result)
    {
        result = str switch
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

    static bool TryParseNumber(string token, out int value) => Int32.TryParse(token, out value);
}