namespace Firesharp;

static partial class Firesharp
{
    static void ParseFile(FileStream file)
    {
        using(StreamReader reader = new StreamReader(file))
        {
            int i = 0;
            while(reader.ReadLine() is string line)
            {
                ParseLine(line, i++);
            }
        }
    }

    static void ParseLine(string line, int current)
    {
        string[] tokens = line.Split(' ');
        int i = 0;
        int tokenPos = 0;
        while (i < tokens.Count())
        {
            string token = tokens[i];
            Loc loc = new Loc(current, tokenPos);
            if(TryParseNumber(token, out int value))
            {
                program.Add(Op.New(OpType.push_int, value, loc));
            }
            else if(TryParseKeyword(token, out KeywordType keyword))
            {
                TryLexToken(keyword, loc)();
            }
            else if(TryParseIntrinsic(token, out IntrinsicType intrinsic))
            {
                program.Add(Op.New(intrinsic, loc));
            }
            else Exit($"could not parse the word `{token}` at line: {current}, pos: {tokenPos}");

            tokenPos += token.Length + 1;
            i++;
        }
    }

    static Action TryLexToken<t>(t token, Loc loc)
        where t : struct, Enum => token switch
    {
        KeywordType.dup  => () => program.Add(Op.New(OpType.dup,  loc)),
        KeywordType.swap => () => program.Add(Op.New(OpType.swap, loc)),
        KeywordType.drop => () => program.Add(Op.New(OpType.drop, loc)),
        KeywordType.over => () => program.Add(Op.New(OpType.over, loc)),
        KeywordType.rot  => () => program.Add(Op.New(OpType.rot,  loc)),
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