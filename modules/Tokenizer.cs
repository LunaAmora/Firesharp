namespace Firesharp;

using System.Text.RegularExpressions;
using static Parser;

class Tokenizer
{   
    record struct Token(string name, Loc loc){}
    
    public ref struct Lexer
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
            if(stream.ReadLine() is string line)
            {
                lineNum++;
                if(string.IsNullOrWhiteSpace(line)) return (ReadLine());

                buffer = line.AsSpan();
                colNum = 0;
                parserPos = 0;
                
                if(!TrimLeft()) return ReadLine();
                return true;
            }
            buffer = ReadOnlySpan<char>.Empty;
            return false;
        }

        void AdvanceByPredicate(Predicate<char> pred)
        {
            while (buffer.Length > parserPos && !pred(buffer[parserPos])) parserPos++;
        }

        string ReadByPredicate(Predicate<char> pred)
        {
            AdvanceByPredicate(pred);
            if(colNum == parserPos) parserPos++;
            return buffer.Slice(colNum, parserPos - colNum).ToString();
        }

        bool TrimLeft()
        {
            if(parserPos > buffer.Length - 1 || buffer.Slice(parserPos).Trim().IsEmpty) return false;

            AdvanceByPredicate(pred => pred != ' ');
            colNum = parserPos;
            if(buffer.Slice(parserPos).StartsWith("//")) return false;

            return true;
        }

        bool NextToken(out Token token)
        {
            if(!TrimLeft() && !ReadLine())
            {
                token = default;
                return false;
            }
            Predicate<char> pred = (c => c == ' ' || c == ':');
            token = new(ReadByPredicate(pred), (file, lineNum, colNum + 1));
            return true;
        }

        bool TryParseString(Token tok, out int index)
        {
            index = dataList.Count;
            if(tok.name is ['\"', ..] name)
            {
                if(name is not [.., '\"'])
                {
                    AdvanceByPredicate(pred => pred == '\"');
                    name = ReadByPredicate(pred => pred == ' ');
                    Assert(name is [.., '\"'], tok.loc, "Missing closing `\"` in string literal");
                }
                
                name = name.Trim('\"');
                var scapes = name.Count(pred => pred == '\\'); //TODO: This does not take escaped '\' into account
                var length = name.Length - scapes;
                dataList.Add(new((name, length)));
                return true;
            }
            return false;
        }

        public IRToken? ParseNextToken() => NextToken(out Token tok) switch
        {
            false
                => (null),
            _ when TryParseChar(tok, out int value)
                => new(TokenType.@int, value, tok.loc),
            _ when TryParseString(tok, out int index)
                => new(TokenType.str, index, tok.loc),
            _ when TryParseNumber(tok.name, out int value)
                => new(TokenType.@int, value, tok.loc),
            _ when TryParseKeyword(tok.name, out int keyword)
                => new(TokenType.keyword, keyword, tok.loc),
            _ => new(TokenType.word, DefineWord(tok.name), tok.loc)
        };

    }

    static int DefineWord(string word)
    {
        wordList.Add(word);
        return wordList.Count - 1;
    }

    static bool TryParseChar(Token tok, out int value)
    {
        switch (tok.name)
        {
            case ['\'', .. var rest, '\''] when Regex.Unescape(rest) is {} code :
                Assert(code.Length == 1, tok.loc, "Char literals cannot contain more than one char");
                value = code[0];
                return true;
            case ['\'', ..] when Error(tok.loc, "Missing closing `\'` in char literal") is {}:
            default : 
                value = -1;
                return false;
        };
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
            "if"   => KeywordType.@if,
            "else" => KeywordType.@else,
            "end"  => KeywordType.end,
            "proc" => KeywordType.proc,
            "->"   => KeywordType.arrow,
            "mem"  => KeywordType.mem,
            ":"    => KeywordType.colon,
            "="    => KeywordType.equal,
            "let"  => KeywordType.let,
            "do"   => KeywordType.@do,
            "@"    => KeywordType.at,
            "case" => KeywordType.@case,
            "while"  => KeywordType.@while,
            "struct" => KeywordType.@struct,
            "include" => KeywordType.include,
            _ => (KeywordType)(-1)
        });
        return result >= 0;
    }
}
