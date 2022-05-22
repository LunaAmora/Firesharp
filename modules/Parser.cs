namespace Firesharp;

static partial class Firesharp
{
    static void ParseFile(FileStream file)
    {
        using(Parser parser = new Parser(file))
        {
            ParseTokens(parser);
        }
    }

    class Parser : IDisposable
    {
        StreamReader reader;
        string[]? tokens;
        
        public int currentToken;
        public int tokenPos;
        public int lineNumber;

        public Parser(FileStream file)
        {
            reader = new StreamReader(file);
        }

        public void NextLine()
        {
            if (reader.ReadLine() is string line)
            {
                tokens = line.Split(' ');
                currentToken = 0;
                tokenPos = 0;
                lineNumber++;
            }
            else
            {
                tokens = null;
            }
        }

        public bool NextToken(out string token)
        {
            if(tokens is null || currentToken >= tokens.Count())
            {
                NextLine();
            }

            if (tokens is null)
            {
                token = "";
                return false;
            }
            
            token = tokens[currentToken++];
            tokenPos += token.Length;
            return true;
        }

        public void Dispose()
        {
            reader.Dispose();
        }
    }

    static void ParseTokens(Parser parser)
    {
        while (parser.NextToken(out string token))
        {
            Loc loc = new Loc(parser.lineNumber, parser.tokenPos);
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
            else Exit($"could not parse the word `{token}` at line: {loc.line}, pos: {loc.pos}");
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