using System.Diagnostics;

namespace Firesharp;

using static TypeChecker;
using static Tokenizer;

class Parser
{
    public static List<Proc> procList = new();
    public static List<TypedWord> varList = new();
    public static int totalVarsSize => varList.Count * 4;
    public static List<StructType> structList = new();

    static List<Op> program = new();
    static Stack<Op> opBlock = new();

    static List<OffsetWord> structVarsList = new();
    static List<TypedWord> constList = new();
    
    static List<OffsetWord> memList = new();
    public static int totalMemSize = 0;

    public static Stack<string> bindStack = new();
    
    static Proc? _currentProc;
    public static bool InsideProc => _currentProc != null;
    public static Proc CurrentProc
    {
        get
        {
            Debug.Assert(_currentProc is {});
            return _currentProc;
        }
        set => _currentProc = value;
    }

    public static void ExitCurrentProc() => _currentProc = null;

    static Queue<IRToken> IRTokens = new();

    public static List<Op> ParseFile(FileStream file, string filepath)
    {
        structList.Add(new("int",  new List<StructMember>(){new("@", TokenType._int)}));
        structList.Add(new("bool", new List<StructMember>(){new("@", TokenType._bool)}));
        structList.Add(new("ptr",  new List<StructMember>(){new("@", TokenType._ptr)}));
        structList.Add(new("any",  new List<StructMember>(){new("@", TokenType._any)}));
        
        using (var reader = new StreamReader(file))
        {
            Lexer lexer = new(reader, filepath);

            while(lexer.ParseNextToken() is {} token)
            {
                IRTokens.Enqueue(token);
            }
            
            while(NextIRToken() is {} token)
            {
                if(DefineOp(token) is {} op)
                {
                    program.Add(op);
                }
            }
        }
        return program;
    }
    
    static IRToken IRTokenAt(int i) => IRTokens.ElementAt(i);

    static IRToken? PeekIRToken()
    {
        if (IRTokens.Count > 0) return IRTokens.First();
        return null;
    }

    static IRToken? NextIRToken()
    {
        if (IRTokens.Count > 0) return IRTokens.Dequeue();
        return null;
    }
    
    static IRToken NextIRTokens(int quantity)
    {
        Assert(IRTokens.Count > quantity, "Unreachable, parser error");
        IRToken result = IRTokens.Dequeue();
        for (int i = 1; i < quantity; i++) result = IRTokens.Dequeue();
        return result;
    }
    
    static bool TryGetIntrinsic(string word, out IntrinsicType result)
    {
        result = (word switch
        {
            "+" => IntrinsicType.plus,
            "-" => IntrinsicType.minus,
            "*" => IntrinsicType.times,
            "%" => IntrinsicType.div,
            "@32" => IntrinsicType.load32,
            "!32" => IntrinsicType.store32,
            "fd_write" => IntrinsicType.fd_write,
            {} when word.StartsWith('#') && TryParseCastType(word, out int cast)
                => IntrinsicType.cast + cast,
            _ => (IntrinsicType)(-1)
        });
        return result >= 0;
    }

    static bool TryParseCastType(string word, out int result)
    {
        word = word.Split('#')[1];
        result = word switch
        {
            "int"  =>  0,
            "bool" =>  1,
            "ptr"  =>  2,
            {} when word.StartsWith('*') && TryParseDataType(word.Split('*')[1], out result) => result,
            {} when TryParseDataType(word, out result) => result,
            _ => -1
        };
        return result >= 0;
    }

    static bool TryParseDataType(string word, out int result)
    {
        if (!(TryGetTypeName(word) is {} type)) result = -1;
        else result = TokenType._struct + structList.IndexOf(type) - TokenType._int;
        return result >= 0;
    }

    public static string TypeNames(TokenType type) => type switch
    {
        TokenType._keyword => "Keyword",
        TokenType._word => "Word",
        TokenType._any  => "Any",
        TokenType._str  => "String",
        TokenType._int  => "Integer",
        TokenType._bool => "Boolean",
        TokenType._ptr  => "Pointer",
        {} typ when typ >= TokenType._struct 
            => $"{structList[typ-TokenType._struct].name} Pointer",
        _ => Error($"DataType name not implemented: {type}")
    };

    static Op? DefineOp(IRToken tok) => tok.type switch
    {
        {} typ when ExpectProc(typ, tok.loc, $"Token type cannot be used outside of a procedure: `{TypeNames(typ)}`") => null,
        TokenType._keyword => DefineOp((KeywordType)tok.operand, tok.loc),
        TokenType._str     => (OpType.push_str, tok.operand, tok.loc),
        TokenType._int     => (OpType.push_int, tok.operand, tok.loc),
        TokenType._bool    => (OpType.push_bool, tok.operand, tok.loc),
        TokenType._ptr     => (OpType.push_ptr, tok.operand, tok.loc),
        TokenType._word    => wordList[tok.operand] switch
        {
            var word when TryGetConstStruct(word, tok.loc) => null,
            var word when TryGetOffset(word, tok.operand, tok.loc) is {} result => result,
            var word when TryGetBinding(word, tok.loc) is {} result => result,
            var word when TryGetIntrinsic(word, tok.loc) is {} result => result,
            var word when TryGetLocalMem(word, tok.loc)  is {} result => result,
            var word when TryGetGlobalMem(word, tok.loc) is {} result => result,
            var word when TryGetProcName(word, tok.loc)  is {} result => result,
            var word when TryGetConstName(word) is {} result => DefineOp(new(result, tok.loc)),
            var word when TryGetLocalVar(word, tok.loc, out Op? result) => result,
            var word when TryGetGlobalVar(word, tok.loc, out Op? result) => result,
            var word when TryDefineContext(word, tok.loc, out Op? result) => result,
            var word => (Op?)Error(tok.loc, $"Word was not declared on the program: `{word}`")
        },
        _ => (Op?)Error(tok.loc, $"Token type not implemented in `DefineOp` yet: {tok.type}")
    };

    static Op? DefineOp(KeywordType type, Loc loc) => type switch
    {
        KeywordType.dup   => (OpType.dup,  loc),
        KeywordType.swap  => (OpType.swap, loc),
        KeywordType.drop  => (OpType.drop, loc),
        KeywordType.over  => (OpType.over, loc),
        KeywordType.rot   => (OpType.rot,  loc),
        KeywordType.equal => (OpType.equal,loc),
        KeywordType.let   => PushBlock(ParseBindings((OpType.bind_stack, loc))),
        KeywordType._if   => PushBlock((OpType.if_start, loc)),
        KeywordType._else => PopBlock(loc, type) switch
        {
            {type: OpType.if_start} => PushBlock((OpType._else, loc)),
            {} op => (Op?)Error(loc, $"`else` can only come after an `if` block, but found a `{op.type}` block instead`",
                $"{op.loc} [INFO] The found block started here")
        },
        KeywordType.end => PopBlock(loc, type) switch
        {
            {type: OpType.if_start} => (OpType.end_if, loc),
            {type: OpType._else}    => (OpType.end_else, loc),
            {type: OpType.prep_proc} op => ExitProc((OpType.end_proc, op.operand, loc)),
            {type: OpType.bind_stack} op => PopBind((OpType.pop_bind, op.operand, loc)),  
            {} op => (Op?)Error(loc, $"`end` can not close a `{op.type}` block")
        },
        _ => (Op?)Error(loc, $"Keyword type not implemented in `DefineOp` yet: {type}")
    };

    static Op? TryGetBinding(string word, Loc loc)
    {
        if(!InsideProc) return null;
        var proc = CurrentProc;
        var index = proc.bindings.FindIndex(bind => bind.Equals(word));
        if(index >= 0)
        {
            return (OpType.push_bind, proc.bindings.Count - 1 - index, loc);
        }
        return null;
    }

    static bool ExpectProc(TokenType type, Loc loc, string errorText)
    {
        return !Assert(type is TokenType._keyword or TokenType._word || InsideProc, loc, errorText);
    }

    static Op PopBind(Op op)
    {
        var proc = CurrentProc;
        proc.bindings.RemoveRange(proc.bindings.Count - op.operand, op.operand);
        return op;
    }

    static Op ExitProc(Op op)
    {
        ExitCurrentProc();
        return op;
    }

    static Op? TryGetIntrinsic(string word, Loc loc)
    {
        if(TryGetIntrinsic(word, out IntrinsicType res))
            return (OpType.intrinsic, (int)res, loc);
        return null;
    }

    static Op? TryGetProcName(string word, Loc loc)
    {
        var index = procList.FindIndex(proc => proc.name.Equals(word));
        return index >= 0 ? (OpType.call, index, loc) : null;
    }

    static bool TryGetConstStruct(string word, Loc loc)
    {
        var found = constList.FindAll(val => val.name.StartsWith($"{word}."));
        found.ForEach(member => 
        {
            var typWord = TryGetConstName(member.name);
            if(typWord is {} tword && DefineOp(new(tword, loc)) is {} op)
                program.Add(op);
        });
        return (found.Count > 0);
    }

    static TypedWord? TryGetConstName(string word)
    {
        var index = constList.FindIndex(cnst => cnst.name.Equals(word));
        if(index >= 0)
        {
            return constList[index];
        }
        return null;
    }

    static Op? TryGetOffset(string word, int index, Loc loc)
    {
        if(word.StartsWith('.'))
        {
            if(word.StartsWith(".*"))
                 return (OpType.offset, index, loc);
            else return (OpType.offset_load, index, loc);
        }
        else return null;
    }

    static Op? TryGetLocalMem(string word, Loc loc)
    {
        if(InsideProc && CurrentProc is {} proc)
        {
            var index = proc.localMemNames.FindIndex(mem => mem.name.Equals(word));
            if (index != - 1)
            {
                return (OpType.push_local_mem, proc.localMemNames[index].offset, loc);
            }
        }
        return null;
    }

    static bool TryGetLocalVar(string word, Loc loc, out Op? result)
    {
        result = null;
        if(!InsideProc) return false;
        var proc = CurrentProc;
        
        var store = false;
        var pointer = false;
        if (word.StartsWith('!'))
        {
            word = word.Split('!')[1];
            store = true;
        }
        else if(word.StartsWith('*'))
        {
            word = word.Split('*')[1];
            pointer = true;
        }

        var index = proc.localVars.FindIndex(val => val.name.Equals(word));
        if (index >= 0)
        {
            program.Add((OpType.push_local, index, loc));
            if (store) program.Add((OpType.intrinsic, (int)IntrinsicType.store32, loc));
            else if(pointer)
            {
                var pointerType = proc.localVars[index].type - TokenType._int + TokenType._struct;
                program.Add((OpType.intrinsic, DataTypeToCast(pointerType), loc));
            }
            else
            {
                program.Add((OpType.intrinsic, (int)IntrinsicType.load32, loc));
                program.Add((OpType.intrinsic, DataTypeToCast(proc.localVars[index].type), loc));
            }
            return true;
        }
        
        index = proc.localVars.FindIndex(val => val.name.StartsWith($"{word}."));
        if (index >= 0 && TryGetStructVars(word) is {} structType)
        {
            if(pointer)
            {
                var member = structType.members.Last().name;
                index = proc.localVars.FindIndex(val => $"{word}.{member}".Equals(val.name));
                var i = structList.IndexOf(structType);
                program.Add((OpType.push_local, index, loc));
                program.Add((OpType.intrinsic, DataTypeToCast(TokenType._struct + i), loc));
            }
            else
            {
                var members = new List<StructMember>(structType.members);
                if(!store) members.Reverse();
                
                index = proc.localVars.FindIndex(val => $"{word}.{members[0].name}".Equals(val.name));
                for (int i = 0; i < members.Count; i++)
                {
                    var member = members[members.Count - 1 - i];
                    program.Add((OpType.push_local, store ? index+i : index-i, loc));
                    if (store) program.Add((OpType.intrinsic, (int)IntrinsicType.store32, loc));
                    else
                    {
                        program.Add((OpType.intrinsic, (int)IntrinsicType.load32, loc));
                        program.Add((OpType.intrinsic, DataTypeToCast(member.type), loc));
                    }
                }
            }
            return true;
        }
        return false;
    }

    static bool TryGetGlobalVar(string word, Loc loc, out Op? result)
    {
        result = null;
        var store = false;
        var pointer = false;
        if (word.StartsWith('!'))
        {
            word = word.Split('!')[1];
            store = true;
        }
        else if(word.StartsWith('*'))
        {
            word = word.Split('*')[1];
            pointer = true;
        }

        var index = varList.FindIndex(val => word.Equals(val.name));
        if (index >= 0)
        {
            program.Add((OpType.push_global, index, loc));
            if (store) program.Add((OpType.intrinsic, (int)IntrinsicType.store32, loc));
            else if(pointer)
            {
                var pointerType = varList[index].type - TokenType._int + TokenType._struct;
                program.Add((OpType.intrinsic, DataTypeToCast(pointerType), loc));
            }
            else
            {
                program.Add((OpType.intrinsic, (int)IntrinsicType.load32, loc));
                program.Add((OpType.intrinsic, DataTypeToCast(varList[index].type), loc));
            }
            return true;
        }

        index = varList.FindIndex(val => val.name.StartsWith($"{word}."));
        if(index >= 0 && TryGetStructVars(word) is {} structType)
        {
            if(pointer)
            {
                var member = structType.members.First().name;
                index = varList.FindIndex(val => $"{word}.{member}".Equals(val.name));
                var i = structList.IndexOf(structType);
                program.Add((OpType.push_global, index, loc));
                program.Add((OpType.intrinsic, DataTypeToCast(TokenType._struct + i), loc));
            }
            else
            {
                var members = new List<StructMember>(structType.members);
                if(store) members.Reverse();
                foreach (var member in members)
                {
                    index = varList.FindIndex(val => $"{word}.{member.name}".Equals(val.name));
                    program.Add((OpType.push_global, index, loc));
                    if (store) program.Add((OpType.intrinsic, (int)IntrinsicType.store32, loc));
                    else
                    {
                        program.Add((OpType.intrinsic, (int)IntrinsicType.load32, loc));
                        program.Add((OpType.intrinsic, DataTypeToCast(member.type), loc));
                    }
                }
            }
            return true;
        }
        result = null;
        return false;
    }

    static StructType? TryGetStructVars(string word)
    {
        var index = structVarsList.FindIndex(vars => vars.name.Equals(word));
        if (index >= 0)
        {
            return TryGetTypeName(wordList[structVarsList[index].offset]);
        }
        return null;
    }

    static StructType? TryGetTypeName(string word)
    {
        var index = structList.FindIndex(type => type.name.Equals(word));
        if (index >= 0) return structList[index];
        return null;
    }
    
    static Op? TryGetGlobalMem(string word, Loc loc)
    {
        var index = memList.FindIndex(mem => mem.name.Equals(word));
        if (index >= 0)
        {
            return (OpType.push_global_mem, memList[index].offset, loc);
        }
        return null;
    }

    static bool TryGetDataPointer(string word, out int dataId)
    {
        if(word.StartsWith('*')) word = word.Split('*')[1];
        else
        {
            dataId = -1;
            return false;
        }
        return (TryParseDataType(word, out dataId));
    }

    static bool TryDefineContext(string word, Loc loc, out Op? result)
    {
        result = null;
        var colonCount = 0;
        KeywordType? context = null;
        for (int i = 0; i < IRTokens.Count; i++)
        {
            var token = IRTokenAt(i);
            if (token.type is not TokenType._keyword)
            {
                if(colonCount == 0)
                {
                    if(token.type is TokenType._word
                        && TryGetTypeName(wordList[token.operand]) is {} structType)
                    {
                        NextIRToken();
                        ParseStructVar(word, loc, token.operand, structType);
                        return true;
                    }
                    return false;
                }
                else if(colonCount == 1 && token.type is TokenType._int)
                {
                    var lastToken = IRTokenAt(i-1);
                    if(lastToken.type is TokenType._keyword 
                        && (KeywordType)lastToken.operand is KeywordType.equal)
                    {
                        context = KeywordType._int;
                    }
                    else context = KeywordType.mem;
                    break;
                }
                else if(colonCount == 1 && token.type is TokenType._word)
                {
                    if (TryGetTypeName(wordList[token.operand]) is {} structType ||
                        TryGetDataPointer(wordList[token.operand], out int _))
                    {
                        context = KeywordType.proc;
                        break;
                    }
                    else if(IRTokens.Count > i+2)
                    {
                        var n1 = IRTokenAt(i+1);
                        var n2 = IRTokenAt(i+2);
                        if(n1 is {type: TokenType._keyword})
                        {
                            if(KeywordToDataType((KeywordType)n1.operand) is not TokenType._keyword && (
                                (n2 is {type: TokenType._keyword} && (KeywordType)n2.operand is KeywordType.end) ||
                                (n2 is {type: TokenType._word})))
                            {
                                context = KeywordType._struct;
                                break;
                            }
                        }
                    }
                }
                
                var invalidToken = token.type is TokenType._word ? 
                    wordList[token.operand] : token.type.ToString();
                Error(token.loc, $"Invalid Token found on context declaration: `{invalidToken}`");
                return false;
            }
            
            context = (KeywordType)token.operand;

            if(colonCount == 0 && (KeywordType.wordTypes | KeywordType.dataTypes).HasFlag(context))
            {
                NextIRToken();
                break;
            }
            else if(context is KeywordType.colon) colonCount++;
            else if(context is KeywordType.end)
            {
                Error(token.loc, $"Missing body or contract necessary to infer the type of the word: `{word}`");
                return false;
            }
            else if(context is KeywordType.equal && colonCount == 1)
            {
                NextIRTokens(2);
                if(CompileEval(out (TypeFrame frame, int value) varEval, out int skip))
                {
                    Assert(varEval.frame.type is not TokenType._any, varEval.frame.loc, "Undefined variable value is not allowed");
                    NextIRTokens(skip);
                    TypedWord newVar = new(word, varEval.value, varEval.frame.type);
                    if (InsideProc) CurrentProc.localVars.Add(newVar);
                    else varList.Add(newVar);
                    return true;
                }
            }

            if(colonCount == 2)
            {
                if(i == 1)
                {
                    NextIRTokens(2);
                    
                    if(CompileEval(out (TypeFrame frame, int value) eval, out int skip))
                    {
                        if(eval.frame.type is not TokenType._any)
                        {
                            NextIRTokens(skip);
                            constList.Add((word, eval.value, eval.frame.type));
                            return true;
                        }
                    }
                    
                    result = DefineProc(word, (OpType.prep_proc, loc), new());
                    return true;
                }
                
                context = KeywordType.proc;
                break;
            }
        }

        return context switch
        {
            KeywordType.proc => ParseProcedure(word, ref result, (OpType.prep_proc, loc)),
            KeywordType.mem  => ParseMemory(word, loc),
            KeywordType._struct => ParseStruct(word, loc), 
            _ when KeywordToDataType(context) is {} tokType
              => ParseConstOrVar(word, loc, tokType, ref result),
            _ => false
        };
    }

    static bool CompileEval(out (TypeFrame frame, int value) result, out int skip)
    {
        var ret = CompileEval(1, out Stack<(TypeFrame frame, int value)> res, out skip);
        result = res.Pop();
        return(ret);
    }

    static bool CompileEval(int quantity, out Stack<(TypeFrame frame, int value)> result, out int skip)
    {
        result = new Stack<(TypeFrame frame, int value)>();
        IRToken token = default;
        for (int i = 0; i < IRTokens.Count; i++)
        {
            token = IRTokenAt(i);
            if(token is {type: TokenType._keyword})
            {
                if((KeywordType)token.operand is KeywordType.end)
                {
                    if(result.Count == 0 && i == 0)
                    {
                        result.Push(((TokenType._any, token.loc), 0));
                        skip = 1;
                        return true;
                    }
                    else if(result.Count != quantity)
                    {
                        var typs = result.Select(f => f.frame).ToList().ListTypes(true);
                        Error(token.loc, $"Expected {quantity} value{(quantity > 1 ? "s" : "")} on the stack in the end of the compile-time evaluation, but found: {typs}");
                        break;
                    }
                    else
                    {
                        skip = i+1;
                        return true;
                    }
                }
            }
            if(!EvalToken(token, result)()) break;
        }
        result.Push(default);
        skip = 0;
        return false;
    }

    static Func<bool> EvalToken(IRToken tok, Stack<(TypeFrame frame, int value)> evalStack) => tok.type switch
    {
        TokenType._int  => () =>
        {
            evalStack.Push(((TokenType._int, tok.loc), tok.operand));
            return true;
        },
        TokenType._bool => () =>
        {
            evalStack.Push(((TokenType._bool, tok.loc), tok.operand));
            return true;
        },
        TokenType._ptr => () =>
        {
            evalStack.Push(((TokenType._ptr, tok.loc), tok.operand));
            return true;
        },
        TokenType._str => () =>
        {
            var data = dataList.ElementAt(tok.operand);
            evalStack.Push(((TokenType._int, tok.loc), data.size));
            evalStack.Push(((TokenType._ptr, tok.loc), data.offset));
            return true;
        },
        TokenType._word => () => (wordList[tok.operand] switch
        {
            var word when TryGetIntrinsic(word, tok.loc) is {} op => () => ((IntrinsicType)op.operand switch
            {
                IntrinsicType.plus => () =>
                {
                    var A = evalStack.Pop();
                    var B = evalStack.Pop();
                    evalStack.Push(((B.frame.type, op.loc), A.value + B.value));
                    return true;
                },
                IntrinsicType.minus => () =>
                {
                    var A = evalStack.Pop();
                    var B = evalStack.Pop();
                    evalStack.Push(((B.frame.type, op.loc), B.value - A.value));
                    return true;
                },
                {} cast when cast >= IntrinsicType.cast => () =>
                {
                    var A = evalStack.Pop();
                    evalStack.Push(((TokenType._int + (int)(cast - IntrinsicType.cast), op.loc), A.value));
                    return true;
                },
                _ => (Func<bool>)(() => false),
            })(),
            var word when TryGetConstName(word) is {} cnst => () =>
            {
                evalStack.Push(((cnst.type, tok.loc), cnst.value));
                return true;
            },
            _ => (Func<bool>)(() => false),
        })(),
        TokenType._keyword => () => ((KeywordType)tok.operand switch
        {
            KeywordType.dup => () =>
            {
                evalStack.Push(evalStack.Peek());
                return true;
            },
            KeywordType.swap => () =>
            {
                var A = evalStack.Pop();
                var B = evalStack.Pop();
                evalStack.Push(A);
                evalStack.Push(B);
                return true;
            },
            KeywordType.drop => () =>
            {
                evalStack.Pop();
                return true;
            },
            KeywordType.over => () =>
            {
                var A = evalStack.Pop();
                var B = evalStack.Pop();
                evalStack.Push(B);
                evalStack.Push(A);
                evalStack.Push(B);
                return true;
            },
            KeywordType.rot => () =>
            {
                var A = evalStack.Pop();
                var B = evalStack.Pop();
                var C = evalStack.Pop();
                evalStack.Push(B);
                evalStack.Push(A);
                evalStack.Push(C);
                return true;
            },
            KeywordType.equal => () => 
            {
                var A = evalStack.Pop();
                var B = evalStack.Pop();
                evalStack.Push(((TokenType._bool, tok.loc), A.value == B.value ? 1 : 0));
                return true;
            },
            _ => (Func<bool>)(() => false),
        })(),
         _ => () => false,
    };

    static void ParseStructVar(string word, Loc loc, int wordIndex, StructType structType)
    {
        ExpectKeyword(loc, KeywordType.colon, "`:` after variable type definition");
        var assign = ExpectKeyword(loc, KeywordType.assignTypes, "`:` or `=` after keyword `:`");

        var members = structType.members;
        var success = CompileEval(members.Count, out Stack<(TypeFrame frame, int value)> result, out int skip);
        Assert(success, loc, "Failed to parse an valid struct value at compile-time evaluation");

        var endToken = NextIRTokens(skip);
        if (!(assign is {operand: int op} && (KeywordType)op is {} keyword))
        {
            Error(loc, "error");
            return;
        }

        if(result.Count == 1 && result.Pop() is {frame: {type: TokenType._any}})
        {
            foreach (var member in members)
            {
                var structVar = ($"{word}.{member.name}", member.defaultValue, member.type);
                if(keyword is KeywordType.colon) constList.Add(structVar);
                else
                {
                    if (InsideProc) CurrentProc.localVars.Add(structVar);
                    else varList.Add(structVar);
                }
            }
        }
        else
        {
            var frames = result.Select(element => element.frame).ToList();
            var expected = members.Select(member => member.type).Reverse().ToArray();
            TypeChecker.ExpectStackExact(frames, endToken.loc, expected);
            
            for (int a = 0; a < members.Count; a++)
            {
                var name = $"{word}.{members[a].name}";
                var item = result.ElementAt(InsideProc ? a : result.Count - 1 - a);
                var structVar = (name, item.value, item.frame.type);
                if(keyword is KeywordType.colon) constList.Add(structVar);
                else
                {
                    if (InsideProc) CurrentProc.localVars.Add(structVar);
                    else varList.Add(structVar);
                }
            }
        }

        structVarsList.Add((word, wordIndex));
    }

    static bool ParseStruct(string word, Loc loc)
    {
        var members = new List<StructMember>();
        ExpectKeyword(loc, KeywordType.colon, "`:` after keyword `struct`");
        while(PeekIRToken() is {} token)
        {
            if(token.type is TokenType._keyword)
            {
                ExpectKeyword(loc, KeywordType.end, "`end` after struct declaration");
                structList.Add(new(word, members));
                return true;
            }

            var name = ExpectToken(loc, TokenType._word, "struct member name");
            var type = ExpectKeyword(loc, KeywordType.dataTypes, "struct member type");

            if(name is {operand: int index} && type is {operand: int keyType}
                && KeywordToDataType((KeywordType)keyType) is {} tokType)
            {
                members.Add(new(wordList[index], tokType));
            }
        }
        Error(loc, "Expected struct members or `end` after struct declaration");
        return false;
    }

    static bool ParseConstOrVar(string word, Loc loc, TokenType tokType, ref Op? result)
    {
        ExpectKeyword(loc, KeywordType.colon, $"`:` after `{tokType}`");
        var assignType = ExpectKeyword(loc, KeywordType.assignTypes, $"`:` or `=` after `{TypeNames(tokType)}`");
        if (assignType is {operand: int op} && (KeywordType)op is {} keyword)
        {
            if (CompileEval(out (TypeFrame frame, int value) eval, out int skip))
            {
                Assert((tokType | TokenType._any).HasFlag(eval.frame.type), $"Expected type `{TypeNames(tokType)}` on the stack at the end of the compile-time evaluation, but found: `{TypeNames(eval.frame.type)}`");
                NextIRTokens(skip);
                TypedWord newVar = new(word, eval.value, tokType);
                if(keyword is KeywordType.colon)
                {
                    constList.Add(newVar);
                    return true;
                }
                else
                {
                    if (InsideProc) CurrentProc.localVars.Add(newVar);
                    else varList.Add(newVar);
                    return true;
                }
            }
            else
            {
                var constOrVarName = keyword is KeywordType.colon ? "constant" : "variable";
                Error(eval.frame.loc, $"Invalid token found on {constOrVarName} declaration: `{TypeNames(eval.frame.type)}`");
            }
        }
        return false;
    }

    static bool ParseProcedure(string name, ref Op? result, Op op)
    {
        List<TokenType> ins = new();
        List<TokenType> outs = new();
        var foundArrow = false;

        ExpectKeyword(op.loc, KeywordType.colon, "Expected `:` after keyword `proc`");
        var sb = new StringBuilder("Expected proc contract or `:` after procedure definition, but found");
        while(NextIRToken() is {} tok)
        {
            if(tok is {type: TokenType._keyword} && (KeywordType)tok.operand is {} typ)
            {
                if (KeywordToDataType(typ) is {} key && key is not TokenType._keyword)
                {
                    if(!foundArrow) ins.Add(key);
                    else outs.Add(key);
                }
                else if (typ is KeywordType.arrow)
                {
                    if(!foundArrow) foundArrow = true;
                    else return Assert(false, tok.loc, "Duplicated `->` found on procedure definition");
                }
                else if (typ is KeywordType.colon)
                {
                    result = DefineProc(name, op, new(ins, outs));
                    return true;
                }
                else
                {
                    sb.Append($": `{typ}`");
                    return Assert(false, tok.loc, sb.ToString());
                }
                continue;
            }
            else if (tok is {type: TokenType._word})
            {
                var foundWord = wordList[tok.operand];
                if (TryGetTypeName(foundWord) is {} structType)
                {
                    foreach (var member in structType.members)
                    {
                        if(!foundArrow) ins.Add(member.type);
                        else outs.Add(member.type);
                    }
                }
                else if (TryGetDataPointer(foundWord, out int dataId))
                {
                    if(!foundArrow) ins.Add(TokenType._int + dataId);
                    else outs.Add(TokenType._int + dataId);
                }
                else
                {
                    sb.Append($" the Word: `{foundWord}`");
                    return Assert(false, tok.loc, sb.ToString());
                }
            }
            else
            {
                sb.Append($": `{TypeNames(tok.type)}`");
                return Assert(false, tok.loc, sb.ToString());
            }
        }
        sb.Append(" nothing");
        return Assert(false, op.loc, sb.ToString());
    }

    static TokenType KeywordToDataType(KeywordType? type) => type switch
    {
        KeywordType._int => TokenType._int,
        KeywordType._ptr => TokenType._ptr,
        KeywordType._bool => TokenType._bool,
        _ => TokenType._keyword
    };

    static int DataTypeToCast(TokenType type)
    {
        if(type < TokenType._int) return -1;
        return (int)IntrinsicType.cast + (int)(type - TokenType._int);
    }

    static Op ParseBindings(Op op)
    {
        Assert(InsideProc, "Bindings cannot be used outside of a procedure");
        var words = new List<string>();
        var proc = CurrentProc;
        while(NextIRToken() is {} tok)
        {
            if(tok is {type: TokenType._keyword})
            {
                if((KeywordType)tok.operand is KeywordType.colon)
                {
                    proc.bindings.AddRange(words.Reverse<string>());
                    op.operand = words.Count;
                    break;
                }
                Error(tok.loc, $"Expected `:` to close binding definition, but found: {(KeywordType)tok.operand}");
            }
            else if(tok is {type: TokenType._word})
            {
                words.Add(wordList[tok.operand]);
            }
            else
            {
                Error(tok.loc, $"Expected only words on binding definition, but found: {TypeNames(tok.type)}");
                break;
            }
        }
        return op;
    }

    static Op? DefineProc(string name, Op op, Contract contract)
    {
        Assert(!InsideProc, op.loc, "Cannot define a procedure inside of another procedure");
        CurrentProc = new(name, contract);
        procList.Add(CurrentProc);
        op.operand = procList.Count - 1;
        
        return PushBlock(op);
    }

    static IRToken? ExpectToken(Loc loc, TokenType expected, string notFound)
    {
        var sb = new StringBuilder();
        var errorLoc = loc;
        if (NextIRToken() is {} token)
        {
            if (token.type.Equals(expected)) return token;

            sb.Append($"Expected {TypeNames(expected)} {notFound}, but found ");
            errorLoc = token.loc;

            if (token.type.Equals(TokenType._word) && TryGetIntrinsic(wordList[token.operand], out IntrinsicType intrinsic))
            {
                sb.Append($"the Intrinsic `{intrinsic}`");
            }
            else sb.Append($"a `{TypeNames(token.type)}`");
        }
        else sb.Append($"Expected {notFound}, but found nothing");
        Error(errorLoc, sb.ToString());
        return null;
    }

    static IRToken? ExpectKeyword(Loc loc, KeywordType expectedType, string notFound)
    {
        var token = ExpectToken(loc, TokenType._keyword, notFound);
        if (token is {} tok && !(expectedType.HasFlag((KeywordType)tok.operand)))
        {
            Error(tok.loc, $"Expected keyword to be `{expectedType}`, but found `{(KeywordType)tok.operand}`");
            return null;
        }
        return token;
    }

    static bool ParseMemory(string word, Loc loc)
    {
        ExpectKeyword(loc, KeywordType.colon, "`:` after `mem`");
        if (ExpectToken(loc, TokenType._int, "memory size after `:`") is {} valueToken)
        {
            ExpectKeyword(loc, KeywordType.end, "`end` after memory size");
            var size = ((valueToken.operand + 3)/4)*4;
            if (InsideProc)
            {
                var proc = CurrentProc;
                proc.localMemNames.Add((word, proc.procMemSize));
                proc.procMemSize += size;
            }
            else
            {
                memList.Add((word, totalMemSize));
                totalMemSize += size;
            }
            return true;
        }
        return false;
    }

    static Op PushBlock(Op op)
    {
        if(op is {} o) opBlock.Push(o);
        return op;
    }

    static Op PopBlock(Loc loc, KeywordType closingType)
    {
        Assert(opBlock.Count > 0, loc, $"There are no open blocks to close with `{closingType}`");
        return opBlock.Pop();
    }

    public struct StructType
    {
        public string name;
        public List<StructMember> members;

        public StructType(string Name, List<StructMember> Members)
        {
            name = Name;
            members = Members;
        }
    }

    public struct StructMember
    {
        public string name;
        public TokenType type;
        public int defaultValue = 0;

        public StructMember(string Name, TokenType Keyword)
        {
            name = Name;
            type = Keyword;
        }

        public StructMember(String Name, TokenType Keyword, int DefaultValue) : this(Name, Keyword)
        {
            defaultValue = DefaultValue;
        }
    }
}
