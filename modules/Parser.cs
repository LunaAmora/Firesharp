using System.Diagnostics;

namespace Firesharp;

class Parser
{
    public static List<StructType> structList = new()
    {
        ("int",  TokenType.@int),
        ("bool", TokenType.@bool),
        ("ptr",  TokenType.ptr),
        ("any",  TokenType.any)
    };

    public static Dictionary<Op, (int, int)> blockContacts = new();
    public static List<SizedWord> dataList = new();
    public static List<TypedWord> varList = new();
    public static List<string> wordList = new();
    public static List<Proc> procList = new();
    public static List<Op> program = new();
    public static int totalDataSize = 0;
    public static int totalMemSize = 0;

    static List<OffsetWord> structVarsList = new();
    static List<TypedWord>  constList = new();
    static List<OffsetWord> memList = new();
    static LinkedList<IRToken> IRTokens = new();
    static Stack<Op> opBlock = new();
    static Proc? _currentProc;

    public static int finalDataSize => ((totalDataSize + 3)/4)*4;
    public static int totalVarsSize => varList.Count * 4;
    public static bool InsideProc   => _currentProc != null;
    public static void ExitCurrentProc() => _currentProc = null;

    public static Proc CurrentProc
    {
        get
        {
            Debug.Assert(_currentProc is {});
            return _currentProc;
        }
        set => _currentProc = value;
    }

    public static void TokenizeFile(FileStream file, string filepath)
    {
        var top = IRTokens.Count > 0 ? IRTokens.First : null;
        using (var reader = new StreamReader(file))
        {
            Tokenizer.Lexer lexer = new(reader, filepath);

            while(lexer.ParseNextToken() is {} token)
            {
                if(top is null) IRTokens.AddLast(token);
                else IRTokens.AddBefore(top, token);
            }            
        }
    }

    public static List<Op> ParseTokens()
    {
        while(NextIRToken() is {} token)
        {
            if(DefineOp(token) is {} op)
            {
                program.Add(op);
            }
        }
        return program;
    }
    
    static IRToken IRTokenAt(int i) => IRTokens.ElementAt(i);

    static IRToken? PeekIRToken()
    {
        if(IRTokens.Count > 0) return IRTokens.First();
        return null;
    }

    static IRToken? NextIRToken()
    {
        if(IRTokens.Count > 0)
        {
            var result = IRTokens.First?.Value;
            IRTokens.RemoveFirst();
            return result;
        }
        else return null;
    }
    
    static IRToken NextIRTokens(int quantity)
    {
        Assert(IRTokens.Count > quantity, error: "Unreachable, parser error");
        for (int i = 0; i < quantity-1; i++) IRTokens.RemoveFirst();
        var result = IRTokens.First();
        IRTokens.RemoveFirst();
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
            ">" => IntrinsicType.greater,
            "<" => IntrinsicType.lesser,
            "@8" => IntrinsicType.load8,
            "!8" => IntrinsicType.store8,
            "@16" => IntrinsicType.load16,
            "!16" => IntrinsicType.store16,
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
            "any"  =>  3,
            {} when word.StartsWith('*') && TryParseDataType(word.Split('*')[1], out result) => result,
            {} when TryParseDataType(word, out result) => result,
            _ => -1
        };
        return result >= 0;
    }

    static bool TryParseDataType(string word, out int result)
    {
        if(!(TryGetTypeName(word, out StructType type))) result = -1;
        else result = TokenType.data_ptr + structList.IndexOf(type) - TokenType.@int;
        return result >= 0;
    }

    public static string TypeNames(TokenType type) => type switch
    {
        TokenType.keyword => "Keyword",
        TokenType.word  => "Word",
        TokenType.str   => "String",
        TokenType.@int  => "Integer",
        TokenType.@bool => "Boolean",
        TokenType.ptr   => "Pointer",
        TokenType.any   => "Any",
        {} typ when typ >= TokenType.data_ptr 
            => $"{structList[typ-TokenType.data_ptr].name} Pointer",
        _ => ErrorHere($"DataType name not implemented: {type}")
    };

    static Op? DefineOp(IRToken tok) => tok.type switch
    {
        {} typ when ExpectProc(typ, tok.loc, $"Token type cannot be used outside of a procedure: `{TypeNames(typ)}`") => null,
        TokenType.keyword => DefineOp((KeywordType)tok.operand, tok.loc),
        TokenType.str     => (OpType.push_str, RegisterString(tok.operand), tok.loc),
        TokenType.@int    => (OpType.push_int, tok.operand, tok.loc),
        TokenType.@bool   => (OpType.push_bool, tok.operand, tok.loc),
        TokenType.ptr     => (OpType.push_ptr, tok.operand, tok.loc),
        TokenType.word    => wordList[tok.operand] switch
        {
            var word when TryGetConstStruct(word, tok.loc) => null,
            var word when TryGetOffset(word, tok.operand, tok.loc) is {} result => result,
            var word when TryGetBinding(word, tok.loc) is {} result => result,
            var word when TryGetIntrinsic(word, tok.loc) is {} result => result,
            var word when TryGetLocalMem(word, tok.loc)  is {} result => result,
            var word when TryGetGlobalMem(word, tok.loc) is {} result => result,
            var word when TryGetProcName(word, tok.loc)  is {} result => result,
            var word when TryGetConstName(word) is {} result => DefineOp(new(result, tok.loc)),
            var word when TryGetVariable(word, tok.loc) => null,
            var word when TryDefineContext(word, tok.loc, out Op? result) => result,
            var word => (Op?)Error(tok.loc, $"Word was not declared on the program: `{word}`")
        },
        _ => (Op?)ErrorHere($"Token type not implemented in `DefineOp` yet: {TypeNames(tok.type)}", tok.loc)
    };

    static Op? DefineOp(KeywordType type, Loc loc) => type switch
    {
        KeywordType.dup   => (OpType.dup,  loc),
        KeywordType.swap  => (OpType.swap, loc),
        KeywordType.drop  => (OpType.drop, loc),
        KeywordType.over  => (OpType.over, loc),
        KeywordType.rot   => (OpType.rot,  loc),
        KeywordType.equal => (OpType.equal,loc),
        KeywordType.at    => (OpType.unpack, loc),
        KeywordType.@while => PushBlock((OpType.@while, loc)),
        KeywordType.@do    => PopBlock(loc, type) switch
        {
            {type: OpType.@while} => PushBlock((OpType.@do, loc)),
            {} op => (Op?)Error(loc, $"`do` can only come after an `while` block, but found a `{op.type}` block instead`",
                $"{op.loc} [INFO] The found block started here")
        },
        KeywordType.let   => PushBlock(ParseBindings((OpType.bind_stack, loc))),
        KeywordType.@if   => PushBlock((OpType.if_start, loc)),
        KeywordType.@else => PopBlock(loc, type) switch
        {
            {type: OpType.if_start} => PushBlock((OpType.@else, loc)),
            {} op => (Op?)Error(loc, $"`else` can only come after an `if` block, but found a `{op.type}` block instead`",
                $"{op.loc} [INFO] The found block started here")
        },
        KeywordType.end => PopBlock(loc, type) switch
        {
            {type: OpType.if_start} => (OpType.end_if, loc),
            {type: OpType.@else}    => (OpType.end_else, loc),
            {type: OpType.@do}      => (OpType.end_while, loc),
            {type: OpType.prep_proc} op => ExitProc((OpType.end_proc, op.operand, loc)),
            {type: OpType.bind_stack} op => PopBind((OpType.pop_bind, op.operand, loc)),  
            {} op => (Op?)Error(loc, $"`end` can not close a `{op.type}` block")
        },
        KeywordType.include => IncludeFile(loc),
        _ => (Op?)ErrorHere($"Keyword type not implemented in `DefineOp` yet: {type}", loc)
    };

    static int RegisterString(int operand)
    {
        var data = dataList[operand];
        if(data.offset == -1)
        {
            // Info(default, "Registering the {2} string {0} at {1}", data.name, totalDataSize, operand);
            data.offset = totalDataSize;
            totalDataSize += data.size;
        }
        return operand;
    }
    
    private static Op? IncludeFile(Loc loc)
    {
        var path = ExpectToken(loc, TokenType.str, "include file name");
        var word = dataList[path.operand];
        TryReadRelative(loc, word.name);
        return null;
    }

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
        return !Assert(type is TokenType.keyword or TokenType.word || InsideProc, loc, errorText);
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
            if(index >= 0)
            {
                return (OpType.push_local_mem, proc.localMemNames[index].offset, loc);
            }
        }
        return null;
    }

    static bool TryGetVariable(string word, Loc loc)
    {
        var store = false;
        var pointer = false;
        
        if(word.StartsWith('!'))
        {
            word = word.Split('!')[1];
            store = true;
        }
        else if(word.StartsWith('*'))
        {
            word = word.Split('*')[1];
            pointer = true;
        }
        
        if(InsideProc && TryGetVar(word, loc, CurrentProc.localVars, true, store, pointer))
            return true;
        return TryGetVar(word, loc, varList, false, store, pointer);
    }

    private static bool TryGetVar(string word, Loc loc, List<TypedWord> vars, bool local, bool store, bool pointer)
    {
        var pushType = local ? OpType.push_local : OpType.push_global;
        var index = vars.FindIndex(val => val.name.Equals(word));
        if(index >= 0)
        {
            if(store) program.Add((OpType.expectType, (int)vars[index].type, loc));

            program.Add((pushType, index, loc));

            if(store) program.Add((OpType.intrinsic, (int)IntrinsicType.store32, loc));
            else if(pointer)
            {
                var pointerType = vars[index].type - TokenType.@int + TokenType.data_ptr;
                program.Add((OpType.intrinsic, DataTypeToCast(pointerType), loc));
            }
            else
            {
                program.Add((OpType.intrinsic, (int)IntrinsicType.load32, loc));
                program.Add((OpType.intrinsic, DataTypeToCast(vars[index].type), loc));
            }
            return true;
        }

        index = vars.FindIndex(val => val.name.StartsWith($"{word}."));
        if(index >= 0 && TryGetStructVars(word, out StructType structType))
        {
            StructMember member = structType.members.First();
            if(pointer)
            {
                index = vars.FindIndex(val => $"{word}.{member.name}".Equals(val.name));
                var data_ptrTypeId = structList.IndexOf(structType);
                // Info(loc, "*{0} -> {1} = {2}", $"{word}.{member.name}", member.type, index);
                program.Add((pushType, index, loc));
                program.Add((OpType.intrinsic, DataTypeToCast(TokenType.data_ptr + data_ptrTypeId), loc));
            }
            else
            {
                var members = new List<StructMember>(structType.members);

                if(store)
                {
                    members.Reverse();
                    member = structType.members.Last();
                }

                index = vars.FindIndex(val => $"{word}.{member.name}".Equals(val.name));
                for (int i = 0; i < members.Count; i++)
                {
                    member = members[i];
                    var operand = index + (local == store ? i : -i);
                    // Info(loc, "{0} -> {1} = {2}", $"{word}.{member.name}", member.type, operand);

                    if(store) program.Add((OpType.expectType, (int)member.type, loc));
                    
                    program.Add((pushType, operand, loc));

                    if(store) program.Add((OpType.intrinsic, (int)IntrinsicType.store32, loc));
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

    static bool TryGetStructVars(string word, out StructType result)
    {
        var index = structVarsList.FindIndex(vars => vars.name.Equals(word));
        if(index >= 0)
        {
            return TryGetTypeName(wordList[structVarsList[index].offset], out result);
        }
        result = default;
        return false;
    }

    static bool TryGetTypeName(string word, out StructType result)
    {
        var index = structList.FindIndex(type => type.name.Equals(word));
        if(index >= 0)
        {
            result = structList[index];
            return true;
        }
        result = default;
        return false;
    }
    
    static Op? TryGetGlobalMem(string word, Loc loc)
    {
        var index = memList.FindIndex(mem => mem.name.Equals(word));
        if(index >= 0)
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
            if(token.type is not TokenType.keyword)
            {
                if(colonCount == 0)
                {
                    if(token.type is TokenType.word
                        && TryGetTypeName(wordList[token.operand], out StructType structType))
                    {
                        NextIRToken();
                        ParseStructVar(word, loc, token.operand, structType);
                        return true;
                    }
                    return false;
                }
                else if(colonCount == 1 && token.type is TokenType.@int)
                {
                    var lastToken = IRTokenAt(i-1);
                    if(lastToken.type is TokenType.keyword 
                        && (KeywordType)lastToken.operand is KeywordType.equal)
                    {
                        context = KeywordType.@int;
                    }
                    else context = KeywordType.mem;
                    break;
                }
                else if(colonCount == 1 && token.type is TokenType.word)
                {
                    if(TryGetTypeName(wordList[token.operand], out StructType _) ||
                       TryGetDataPointer(wordList[token.operand], out int _))
                    {
                        context = KeywordType.proc;
                        break;
                    }
                    else if(IRTokens.Count > i+2)
                    {
                        var n1 = IRTokenAt(i+1);
                        var n2 = IRTokenAt(i+2);
                        if(n1 is {type: TokenType.keyword})
                        {
                            if(KeywordToDataType((KeywordType)n1.operand) is not TokenType.keyword && (
                                (n2 is {type: TokenType.keyword} && (KeywordType)n2.operand is KeywordType.end) ||
                                (n2 is {type: TokenType.word})))
                            {
                                context = KeywordType.@struct;
                                break;
                            }
                        }
                    }
                }
                
                var invalidToken = token.type is TokenType.word ? 
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
                    Assert(varEval.frame.type is not TokenType.any, varEval.frame.loc, "Undefined variable value is not allowed");
                    NextIRTokens(skip);
                    TypedWord newVar = (word, varEval.value, varEval.frame.type);
                    if(InsideProc) CurrentProc.localVars.Add(newVar);
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
                        if(eval.frame.type is not TokenType.any)
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
            KeywordType.@struct => ParseStruct(word, loc), 
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
        skip = 0;
        for (int i = 0; i < IRTokens.Count; i++)
        {
            token = IRTokenAt(i);
            if(token is {type: TokenType.keyword})
            {
                if((KeywordType)token.operand is KeywordType.end)
                {
                    if(result.Count == 0 && i == 0)
                    {
                        result.Push(((TokenType.any, token.loc), 0));
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
            if(!EvalToken(token, result)())
            {
                var errorTok = result.Pop();
                result.Clear();
                result.Push(errorTok);
                return false;
            }
        }
        result.Push(default);
        return false;
    }

    static Func<bool> EvalToken(IRToken tok, Stack<(TypeFrame frame, int value)> evalStack) => tok.type switch
    {
        TokenType.@int  => () =>
        {
            evalStack.Push(((TokenType.@int, tok.loc), tok.operand));
            return true;
        },
        TokenType.@bool => () =>
        {
            evalStack.Push(((TokenType.@bool, tok.loc), tok.operand));
            return true;
        },
        TokenType.ptr => () =>
        {
            evalStack.Push(((TokenType.ptr, tok.loc), tok.operand));
            return true;
        },
        TokenType.str => () =>
        {
            RegisterString(tok.operand);
            var data = dataList.ElementAt(tok.operand);
            evalStack.Push(((TokenType.@int, tok.loc), data.size));
            evalStack.Push(((TokenType.ptr, tok.loc), data.offset));
            return true;
        },
        TokenType.word => () => (wordList[tok.operand] switch
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
                    evalStack.Push(((TokenType.@int + (int)(cast - IntrinsicType.cast), op.loc), A.value));
                    return true;
                },
                _ => (Func<bool>)(() => 
                {
                    evalStack.Push(((TokenType.word, tok.loc), tok.operand));
                    return false;
                }),
            })(),
            var word when TryGetConstName(word) is {} cnst => () =>
            {
                evalStack.Push(((cnst.type, tok.loc), cnst.value));
                return true;
            },
            _ => (Func<bool>)(() => 
            {
                evalStack.Push(((TokenType.word, tok.loc), tok.operand));
                return false;
            }),
        })(),
        TokenType.keyword => () => ((KeywordType)tok.operand switch
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
                evalStack.Push(((TokenType.@bool, tok.loc), A.value == B.value ? 1 : 0));
                return true;
            },
            _ => (Func<bool>)(() => 
            {
                evalStack.Push(((TokenType.keyword, tok.loc), tok.operand));
                return false;
            }),
        })(),
         _ => () => false,
    };

    static void ParseStructVar(string word, Loc loc, int wordIndex, StructType structType)
    {
        ExpectKeyword(loc, KeywordType.colon, "`:` after variable type definition");
        var assign = ExpectKeyword(loc, KeywordType.assignTypes, "`:` or `=` after keyword `:`");
        var keyword = (KeywordType)assign.operand;

        var members = new List<StructMember>(structType.members);
        var success = CompileEval(members.Count, out Stack<(TypeFrame frame, int value)> result, out int skip);
        Assert(success, loc, "Failed to parse an valid struct value at compile-time evaluation");

        var endToken = NextIRTokens(skip);

        members.Reverse();
        if(result.Count == 1 && result.Pop() is {frame.type: TokenType.any})
        {
            foreach (var member in members)
            {
                var name = $"{word}.{member.name}";
                var structVar = (name, member.defaultValue, member.type);
                // Info(loc, "Adding var {0} of type {1}", name, member.type);
                if(keyword is KeywordType.colon) constList.Add(structVar);
                else
                {
                    if(InsideProc) CurrentProc.localVars.Add(structVar);
                    else varList.Add(structVar);
                }
            }
        }
        else
        {
            var frames = result.Select(element => element.frame).ToList();
            var expected = members.Select(member => member.type).ToArray();
            TypeChecker.ExpectStackExact(frames, endToken.loc, expected);

            if(!InsideProc) members.Reverse();
            for (int a = 0; a < members.Count; a++)
            {
                var name = $"{word}.{members[a].name}";
                var item = result.ElementAt(InsideProc ? a : result.Count - 1 - a);
                // Info(loc, "Adding var {0} of type {1} and value {2}", name, item.frame.type, item.value);
                var structVar = (name, item.value, item.frame.type);
                if(keyword is KeywordType.colon) constList.Add(structVar);
                else
                {
                    if(InsideProc) CurrentProc.localVars.Add(structVar);
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
            if(token.type is TokenType.keyword)
            {
                ExpectKeyword(loc, KeywordType.end, "`end` after struct declaration");
                structList.Add((word, members));
                return true;
            }

            var index = ExpectToken(loc, TokenType.word, "struct member name").operand;
            if(NextIRToken() is {} nameType)
            {
                var errorText = "Expected struct member type but found";
                var foundWord = wordList[index];
                if(nameType is {type: TokenType.keyword})
                {
                    var key = (KeywordType)nameType.operand;
                    if(KeywordType.dataTypes.HasFlag(key))
                    {
                        members.Add((foundWord, KeywordToDataType(key)));
                    }
                    else Error(loc, $"{errorText} the Keyword: `{key}`");
                }
                else if(nameType is {type: TokenType.word, operand: int typeIndex})
                {
                    var foundType = wordList[typeIndex];
                    if(TryGetTypeName(foundType, out StructType structType))
                    {
                        foreach (var member in structType.members)
                            members.Add(($"{foundWord}.{member.name}", member.type));
                    }
                    else if(TryGetDataPointer(foundType, out int dataId))
                    {
                        members.Add((foundWord, TokenType.@int + dataId));
                    }
                    else Error(nameType.loc, $"{errorText} the Word: `{foundType}`");
                }
                else Error(nameType.loc, $"{errorText}: `{TypeNames(nameType.type)}`");
            }
        }
        Error(loc, "Expected struct members or `end` after struct declaration");
        return false;
    }

    static bool ParseConstOrVar(string word, Loc loc, TokenType tokType, ref Op? result)
    {
        ExpectKeyword(loc, KeywordType.colon, $"`:` after `{tokType}`");
        var assignType = ExpectKeyword(loc, KeywordType.assignTypes, $"`:` or `=` after `{TypeNames(tokType)}`");
        var keyword = (KeywordType)assignType.operand;
        
        if(CompileEval(out (TypeFrame frame, int value) eval, out int skip))
        {
            var endToken = NextIRTokens(skip);
            Assert((tokType | TokenType.any).HasFlag(eval.frame.type), endToken.loc,
                $"Expected type `{TypeNames(tokType)}` on the stack at the end of the compile-time evaluation, but found: `{TypeNames(eval.frame.type)}`");
            TypedWord newVar = (word, eval.value, tokType);
            if(keyword is KeywordType.colon)
            {
                constList.Add(newVar);
            }
            else
            {
                if(InsideProc) CurrentProc.localVars.Add(newVar);
                else varList.Add(newVar);
            }
            return true;
        }

        var foundType = eval.frame.type;
        var declarationType = (keyword is KeywordType.colon ? "constant " : "variable ") + TypeNames(tokType);
        var (foundDesc, foundName) = foundType switch
        {
            TokenType.keyword => ("keyword", ((KeywordType)eval.value).ToString()),
            TokenType.word    => ("word or intrinsic", wordList[eval.value]),
                            _ => ("token", TypeNames(foundType))
        };

        Error(eval.frame.loc, $"Invalid {foundDesc} found on {declarationType} declaration: `{foundName}`");
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
            if(tok is {type: TokenType.keyword} && (KeywordType)tok.operand is {} typ)
            {
                if(KeywordToDataType(typ) is {} key && key is not TokenType.keyword)
                {
                    if(!foundArrow) ins.Add(key);
                    else outs.Add(key);
                }
                else if(typ is KeywordType.arrow)
                {
                    Assert(!foundArrow, tok.loc, "Duplicated `->` found on procedure definition");
                    foundArrow = true;
                }
                else if(typ is KeywordType.colon)
                {
                    result = DefineProc(name, op, new(ins, outs));
                    return true;
                }
                else
                {
                    sb.Append($": `{typ}`");
                    Error(tok.loc, sb.ToString());
                }
                continue;
            }
            else if(tok is {type: TokenType.word})
            {
                var foundWord = wordList[tok.operand];
                if(TryGetTypeName(foundWord, out StructType structType))
                {
                    foreach (var member in structType.members)
                    {
                        if(!foundArrow) ins.Add(member.type);
                        else outs.Add(member.type);
                    }
                }
                else if(TryGetDataPointer(foundWord, out int dataId))
                {
                    if(!foundArrow) ins.Add(TokenType.@int + dataId);
                    else outs.Add(TokenType.@int + dataId);
                }
                else
                {
                    sb.Append($" the Word: `{foundWord}`");
                    Error(tok.loc, sb.ToString());
                }
            }
            else
            {
                sb.Append($": `{TypeNames(tok.type)}`");
                Error(tok.loc, sb.ToString());
            }
        }
        sb.Append(" nothing");
        Error(op.loc, sb.ToString());
        return false;
    }

    static TokenType KeywordToDataType(KeywordType? type) => type switch
    {
        KeywordType.@int => TokenType.@int,
        KeywordType.ptr => TokenType.ptr,
        KeywordType.@bool => TokenType.@bool,
        _ => TokenType.keyword
    };

    static int DataTypeToCast(TokenType type)
    {
        if(type < TokenType.@int) return -1;
        return (int)IntrinsicType.cast + (int)(type - TokenType.@int);
    }

    static Op ParseBindings(Op op)
    {
        Assert(InsideProc, op.loc, "Bindings cannot be used outside of a procedure");
        var words = new List<string>();
        var proc = CurrentProc;
        while(NextIRToken() is {} tok)
        {
            if(tok is {type: TokenType.keyword})
            {
                if((KeywordType)tok.operand is KeywordType.colon)
                {
                    proc.bindings.AddRange(words.Reverse<string>());
                    op.operand = words.Count;
                    break;
                }
                Error(tok.loc, $"Expected `:` to close binding definition, but found: {(KeywordType)tok.operand}");
            }
            else if(tok is {type: TokenType.word})
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

    static IRToken ExpectToken(Loc loc, TokenType expected, string notFound)
    {
        var sb = new StringBuilder();
        var errorLoc = loc;
        if(NextIRToken() is {} token)
        {
            if(token.type.Equals(expected)) return token;

            sb.Append($"Expected {TypeNames(expected)} {notFound}, but found ");
            errorLoc = token.loc;

            if(token.type.Equals(TokenType.word) && TryGetIntrinsic(wordList[token.operand], out IntrinsicType intrinsic))
            {
                sb.Append($"the Intrinsic `{intrinsic}`");
            }
            else sb.Append($"a `{TypeNames(token.type)}`");
        }
        else sb.Append($"Expected {notFound}, but found nothing");
        Error(errorLoc, sb.ToString());
        return default;
    }

    static IRToken ExpectKeyword(Loc loc, KeywordType expectedType, string notFound)
    {
        var tok = ExpectToken(loc, TokenType.keyword, notFound);
        if(!(expectedType.HasFlag((KeywordType)tok.operand)))
        {
            Error(tok.loc, $"Expected keyword to be `{expectedType}`, but found `{(KeywordType)tok.operand}`");
            return default;
        }
        return tok;
    }

    static bool ParseMemory(string word, Loc loc)
    {
        ExpectKeyword(loc, KeywordType.colon, "`:` after `mem`");
        var valueToken = ExpectToken(loc, TokenType.@int, "memory size after `:`");
        ExpectKeyword(loc, KeywordType.end, "`end` after memory size");
        var size = ((valueToken.operand + 3)/4)*4;
        if(InsideProc)
        {
            var proc = CurrentProc;
            proc.procMemSize += size;
            proc.localMemNames.Add((word, proc.procMemSize));
        }
        else
        {
            memList.Add((word, totalMemSize));
            totalMemSize += size;
        }
        return true;
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
}
