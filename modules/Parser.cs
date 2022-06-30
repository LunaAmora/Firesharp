using System.Diagnostics;

namespace Firesharp;

static class Parser
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
    public static int totalDataSize = 0;
    public static int totalMemSize = 0;

    static List<TypedWord> constList = new();
    static List<Word> structVarsList = new();
    static List<Word> memList = new();

    static LinkedList<IRToken> IRTokens = new();
    static Stack<Op> opBlock = new();
    static List<Op> program = new();
    static Proc? _currentProc;

    public static int finalDataSize => ((totalDataSize + 3)/4)*4;
    public static int totalVarsSize => varList.Count * 4;
    public static void ExitCurrentProc() => _currentProc = null;
    
    static bool InsideProc => _currentProc != null;

    public static Proc CurrentProc
    {
        get
        {
            Debug.Assert(_currentProc is {}, "Unreachable, parser error. Tried to access the current procedure outside of one");
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
            if(DefineOp(token) is {} op)
                program.Add(op);
        return program;
    }
    
    static IRToken IRTokenAt(int i) => IRTokens.ElementAt(i);

    static IRToken? PeekIRToken()
    {
        if(IRTokens.Count is 0) return null;
        return IRTokens.First();
    }

    static IRToken? NextIRToken()
    {
        if(IRTokens.Count is 0) return null;
        var result = IRTokens.First?.Value;
        IRTokens.RemoveFirst();
        return result;
    }
    
    static IRToken NextIRTokens(int quantity)
    {
        Assert(IRTokens.Count > quantity, error: "Unreachable, parser error");
        for (int i = 0; i < quantity-1; i++) IRTokens.RemoveFirst();
        var result = IRTokens.First();
        IRTokens.RemoveFirst();
        return result;
    }
    
    static IRToken[] IRTokensUntilPred(Predicate<IRToken> pred)
    {
        int i = 0;
        while(IRTokens.Count > i && !pred(IRTokenAt(i))) i++;
        var result = new IRToken[i];
        for (int a = 0; a < i; a++)
        {
            result[a] = IRTokens.First();
            IRTokens.RemoveFirst();
        }
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
            ">"  => IntrinsicType.greater,
            ">=" => IntrinsicType.greater_e,
            "<"  => IntrinsicType.lesser,
            "<=" => IntrinsicType.lesser_e,
            "or"  => IntrinsicType.or,
            "and" => IntrinsicType.and,
            "xor" => IntrinsicType.xor,
            "@8"  => IntrinsicType.load8,
            "!8"  => IntrinsicType.store8,
            "@16" => IntrinsicType.load16,
            "!16" => IntrinsicType.store16,
            "@32" => IntrinsicType.load32,
            "!32" => IntrinsicType.store32,
            "fd_write" => IntrinsicType.fd_write,
            ['#', .. var rest] when TryParseCastType(rest, out int cast)
                => IntrinsicType.cast + cast,
            _ => (IntrinsicType)(-1)
        });
        return result >= 0;
    }

    static bool TryParseCastType(string word, out int result)
    {
        result = word switch
        {
            "int"  =>  0,
            "bool" =>  1,
            "ptr"  =>  2,
            "any"  =>  3,
            ['*', .. var rest] 
               when TryParseDataType(rest, out result) => result,
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
            var word when TryGetConstName(word, out var cnst) => DefineOp(new(cnst, tok.loc)),
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
            {type: OpType.case_match} op => PushBlock((OpType.case_option, op.operand, loc)),
            {} op => InvalidBlock(loc, op, "`do` can only come in a `while` or `case` block"),
        },
        KeywordType.let   => PushBlock(ParseBindings((OpType.bind_stack, loc))),
        KeywordType.@case => StartCase((OpType.case_start, loc)),
        KeywordType.colon => PopBlock(loc, type) switch
        {
            {type: OpType.case_start} => ParseCaseMatch((OpType.case_match, loc)),
            {} op => InvalidBlock(loc, op, "`:` can only be used on word or `case` block definition"),
        },
        KeywordType.@if   => PushBlock((OpType.if_start, loc)),
        KeywordType.@else => PopBlock(loc, type) switch
        {
            {type: OpType.if_start} => PushBlock((OpType.@else, loc)),
            {type: OpType.case_option} => ParseCaseMatch((OpType.case_match, loc)),
            {} op => InvalidBlock(loc, op, "`else` can only come in a `if` or `case` block"),
        },
        KeywordType.end => PopBlock(loc, type) switch
        {
            {type: OpType.if_start} => (OpType.end_if, loc),  //TODO: Check for constants at the top of the stack, collapse/remove block if true
            {type: OpType.@else}    => (OpType.end_else, loc),
            {type: OpType.@do}      => (OpType.end_while, loc),
            {type: OpType.case_option} => EndCase((OpType.end_case, loc)),
            {type: OpType.prep_proc} op => ExitProc((OpType.end_proc, op.operand, loc)),
            {type: OpType.bind_stack} op => PopBind((OpType.pop_bind, op.operand, loc)),
            {} op => InvalidBlock(loc, op, "Expected `end` to close a valid block"),
        },
        KeywordType.include => IncludeFile(loc),
        _ => (Op?)ErrorHere($"Keyword type not implemented in `DefineOp` yet: {type}", loc)
    };

    static Op? InvalidBlock(Loc loc, Op op, string error) => 
        (Op?)Error(loc, $"{error}, but found a `{op.type}` block instead`",
                        $"{op.loc} [INFO] The found block started here");

    static bool ExpectProc(TokenType type, Loc loc, string errorText)
        => !Assert(type is TokenType.keyword or TokenType.word || InsideProc, loc, errorText);

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
    
    static Op? IncludeFile(Loc loc)
    {
        var path = ExpectNextToken(loc, TokenType.str, "include file name");
        var word = dataList[path.operand];
        TryReadRelative(loc, word.name);
        return null;
    }

    static Op PushBlock(Op op)
    {
        opBlock.Push(op);
        return op;
    }

    static Op PopBlock(Loc loc, KeywordType closingType)
    {
        Assert(opBlock.Count > 0, loc, $"There are no open blocks to close with `{closingType}`");
        return opBlock.Pop();
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

    static Op StartCase(Op op)
    {
        var proc = CurrentProc;
        proc.caseBlocks.Add(new());
        op.operand = proc.caseBlocks.Count() - 1;
        proc.currentBlock += 1;
        return PushBlock(PushBlock(op));
    }

    static Op EndCase(Op op)
    {
        var proc = CurrentProc;
        Assert(proc.caseBlocks[proc.currentBlock]
               .Last().type is CaseType.@default,
               op.loc, "Expected last match of a `case` block to be `_`");
        op.operand = proc.currentBlock--;
        Assert(PopBlock(op.loc, KeywordType.end).type is OpType.case_start, op.loc, $"Unreachable, parser error");
        return op;
    }

    static Op? TryGetBinding(string word, Loc loc)
    {
        if(!InsideProc) return null;
        var proc = CurrentProc;
        var index = proc.bindings.FindIndex(bind => bind.Equals(word));
        if(index < 0) return null;
        return (OpType.push_bind, proc.bindings.Count - 1 - index, loc);
    }

    static Op? TryGetIntrinsic(string word, Loc loc)
    {
        if(!TryGetIntrinsic(word, out IntrinsicType res)) return null;
        return (OpType.intrinsic, (int)res, loc);
    }

    static Op? TryGetProcName(string word, Loc loc)
    {
        var index = procList.FindIndex(proc => proc.name.Equals(word));
        if(index < 0) return null;
        return (OpType.call, index, loc);
    }

    static bool TryGetConstStruct(string word, Loc loc)
    {
        var found = constList.FindAll(val => val.name.StartsWith($"{word}."));
        found.ForEach(member => 
        {
            if(TryGetConstName(member.name, out TypedWord tword) &&
                DefineOp(new(tword, loc)) is {} op)
                program.Add(op);
        });
        return (found.Count > 0);
    }

    static bool TryGetConstName(string word, out TypedWord result)
    {
        var index = constList.FindIndex(cnst => cnst.name.Equals(word));
        if(index >= 0)
        {
            result = constList[index];
            return true;
        }
        result = default;
        return false;
    }

    static Op? TryGetOffset(string word, int index, Loc loc) => word switch
    {
        ['.', '*', ..] => (OpType.offset, index, loc),
        ['.', ..] => (OpType.offset_load, index, loc),
        _ => null,
    };

    static Op? TryGetLocalMem(string word, Loc loc)
    {
        if(InsideProc && CurrentProc is {} proc)
        {
            var index = proc.localMemNames.FindIndex(mem => mem.name.Equals(word));
            if(index >= 0)
                return (OpType.push_local_mem, proc.localMemNames[index].value, loc);
        }
        return null;
    }

    enum VarState { none, store, pointer }

    static bool TryGetVariable(string word, Loc loc)
    {
        (word, VarState wordState) = word switch
        {
            ['!', .. var rest] => (rest, VarState.store),
            ['*', .. var rest] => (rest, VarState.pointer),
            _ => (word, VarState.none)
        };

        return (InsideProc && 
               TryGetVar(word, loc, CurrentProc.localVars, true, wordState)) ||
               TryGetVar(word, loc, varList, false, wordState);
    }

    static bool TryGetVar(string word, Loc loc, List<TypedWord> vars, bool local, VarState varState)
    {
        var pushType = local ? OpType.push_local : OpType.push_global;
        bool store = varState.HasFlag(VarState.store);
        bool pointer = varState.HasFlag(VarState.pointer);
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
    
    static int DataTypeToCast(TokenType type)
    {
        if(type < TokenType.@int) return -1;
        return (int)IntrinsicType.cast + (int)(type - TokenType.@int);
    }

    static bool TryGetStructVars(string word, out StructType result)
    {
        var index = structVarsList.FindIndex(vars => vars.name.Equals(word));
        if(index >= 0)
        {
            return TryGetTypeName(wordList[structVarsList[index].value], out result);
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
        if(index < 0) return null;
        return (OpType.push_global_mem, memList[index].value, loc);
    }

    static bool TryGetDataPointer(string word, out TokenType typePtr)
    {
        if(word is ['*', .. var rest])
        {
            word = rest;
            var success = TryParseDataType(word, out int id);
            typePtr = TokenType.@int + id;
            return success;
        }
        typePtr = (TokenType)(-1);
        return false;
    }

    static bool TryParseRange(string word, Loc loc, out (int start, int end) range)
    {
        if(word.Contains(".."))
        {
            (bool success, int val)[] parts = 
                word.Split("..")
                .Select(part => 
                {
                    if (Int32.TryParse(part, out int a)) return (true, a);
                    else if(TryGetConstName(part, out TypedWord res) &&
                            res.type is TokenType.@int)  return (true, res.value);
                    return (false, 0);
                })
                .ToArray();
            var success = parts.Count() is 2 && parts[0].success && parts[1].success;
            range = (success) ? (parts[0].val, parts[1].val) : default;
            Assert(success && range.start <= range.end, loc, "Invalid range declaration");
            return true;
        }
        range = default;
        return false;
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
                    if(token.type is TokenType.word &&
                        TryGetTypeName(wordList[token.operand], out StructType structType))
                    {
                        NextIRToken();
                        ParseConstOrVar(word, loc, token.operand, structType);
                        return true;
                    }
                    return false;
                }
                else if(colonCount == 1 && token.type is TokenType.@int)
                {
                    context = KeywordType.mem;
                    break;
                }
                else if(colonCount == 1 && token.type is TokenType.word)
                {
                    var foundWord = wordList[token.operand];
                    if(TryGetTypeName(foundWord, out _) ||
                       TryGetDataPointer(foundWord, out _))
                    {
                        context = KeywordType.proc;
                        break;
                    }
                    else if(IRTokens.Count > i+2)
                    {
                        var n1 = IRTokenAt(i+1);
                        var n2 = IRTokenAt(i+2);
                        if(n1.type is TokenType.word)
                        {
                            if(TryGetTypeName(wordList[n1.operand], out _) && 
                                ((n2.type is TokenType.keyword && (KeywordType)n2.operand is KeywordType.end) ||
                                (n2.type is TokenType.word)))
                            {
                                context = KeywordType.@struct;
                                break;
                            }
                        }
                    }
                }
                InvalidToken(token, "context declaration");
                return false;
            }
            
            context = (KeywordType)token.operand;

            if(colonCount == 0 && KeywordType.wordTypes.HasFlag(context))
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
                if(CompileEval(out IRToken varEval, out int skip))
                {
                    Assert(varEval.type is not TokenType.any, varEval.loc, "Undefined variable value is not allowed");
                    NextIRTokens(skip);
                    TypedWord newVar = (word, varEval.operand, varEval.type);
                    if(InsideProc) CurrentProc.localVars.Add(newVar);
                    else varList.Add(newVar);
                    return true;
                }
                
                InvalidToken(token, "context declaration");
                return false;
            }

            if(colonCount == 2)
            {
                if (i != 1)
                {
                    context = KeywordType.proc;
                    break;
                }
                
                NextIRTokens(2);

                if (CompileEval(out IRToken eval, out int skip) &&
                    eval.type is not TokenType.any)
                {
                    NextIRTokens(skip);
                    constList.Add((word, eval.operand, eval.type));
                    return true;
                }

                result = DefineProc(word, (OpType.prep_proc, loc), new());
                return true;
            }
        }

        return context switch
        {
            KeywordType.proc => ParseProcedure(word, ref result, (OpType.prep_proc, loc)),
            KeywordType.mem  => ParseMemory(word, loc),
            KeywordType.@struct => ParseStruct(word, loc), 
            _ => false
        };
    }

    static Op? DefineProc(string name, Op op, Contract contract)
    {
        Assert(!InsideProc, op.loc, "Cannot define a procedure inside of another procedure");
        CurrentProc = new(name, contract);
        procList.Add(CurrentProc);
        op.operand = procList.Count - 1;
        return PushBlock(op);
    }

    static void InvalidToken(IRToken eval, string errorContext)
    {
        var foundType = eval.type;
        var (foundDesc, foundName) = foundType switch
        {
            TokenType.keyword => ("keyword", ((KeywordType)eval.operand).ToString()),
            TokenType.word    => ("word or intrinsic", wordList[eval.operand]),
                            _ => ("token", TypeNames(foundType))
        };
        Error(eval.loc, $"Invalid {foundDesc} found on {errorContext}: `{foundName}`");
    }

    static bool CompileEval(out IRToken result, out int skip)
    {
        var ret = CompileEval(1, out Stack<IRToken> res, out skip);
        result = res.Pop();
        return(ret);
    }

    static bool CompileEval(int quantity, out Stack<IRToken> result, out int skip)
    {
        result = new Stack<IRToken>();
        IRToken token = default;
        skip = 0;
        for (int i = 0; i < IRTokens.Count; i++)
        {
            token = IRTokenAt(i);
            if(token.type is TokenType.keyword)
            {
                if((KeywordType)token.operand is KeywordType.end)
                {
                    if(result.Count == 0 && i == 0)
                        result.Push((TokenType.any, 0, token.loc));
                    else if(result.Count != quantity)
                    {
                        var typs = result.Select(f => (TypeFrame)f).ToList().ListTypes(true);
                        Error(token.loc, $"Expected {quantity} value{(quantity > 1 ? "s" : "")} on the stack in the end of the compile-time evaluation, but found: {typs}");
                        break;
                    }
                    skip = i+1;
                    return true;
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

    static Func<bool> EvalToken(IRToken tok, Stack<IRToken> evalStack) => tok.type switch
    {
        TokenType.@int  => () =>
        {
            evalStack.Push((TokenType.@int, tok.operand, tok.loc));
            return true;
        },
        TokenType.@bool => () =>
        {
            evalStack.Push((TokenType.@bool, tok.operand, tok.loc));
            return true;
        },
        TokenType.ptr => () =>
        {
            evalStack.Push((TokenType.ptr, tok.operand, tok.loc));
            return true;
        },
        TokenType.str => () =>
        {
            RegisterString(tok.operand);
            var data = dataList.ElementAt(tok.operand);
            evalStack.Push((TokenType.@int, data.size, tok.loc));
            evalStack.Push((TokenType.ptr, data.offset, tok.loc));
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
                    evalStack.Push((B.type, A.operand + B.operand, op.loc));
                    return true;
                },
                IntrinsicType.minus => () =>
                {
                    var A = evalStack.Pop();
                    var B = evalStack.Pop();
                    evalStack.Push((B.type, B.operand - A.operand, op.loc));
                    return true;
                },
                {} cast when cast >= IntrinsicType.cast => () =>
                {
                    var A = evalStack.Pop();
                    evalStack.Push((TokenType.@int + (int)(cast - IntrinsicType.cast), A.operand, op.loc));
                    return true;
                },
                _ => (Func<bool>)(() => 
                {
                    evalStack.Push((TokenType.word, tok.operand, tok.loc));
                    return false;
                }),
            })(),
            var word when TryGetConstName(word, out var cnst)=> () =>
            {
                evalStack.Push((cnst.type, cnst.value, tok.loc));
                return true;
            },
            _ => (Func<bool>)(() => 
            {
                evalStack.Push((TokenType.word, tok.operand, tok.loc));
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
                evalStack.Push((TokenType.@bool, A.operand == B.operand ? 1 : 0, tok.loc));
                return true;
            },
            _ => (Func<bool>)(() => 
            {
                evalStack.Push((TokenType.keyword, tok.operand, tok.loc));
                return false;
            }),
        })(),
        _ => () => false,
    };

    static void ParseConstOrVar(string word, Loc loc, int wordIndex, StructType structType)
    {
        ExpectNextKeyword(loc, KeywordType.colon, "`:` after variable type definition");
        var assign = ExpectNextKeyword(loc, KeywordType.assignTypes, "`:` or `=` after keyword `:`");
        var keyword = (KeywordType)assign.operand;

        var members = new List<StructMember>(structType.members);
        var success = CompileEval(members.Count, out Stack<IRToken> result, out int skip);
        Assert(success, loc, "Failed to parse an valid struct value at compile-time evaluation");

        var endToken = NextIRTokens(skip);

        members.Reverse();
        if(result.Count == 1 && result.Pop() is {} eval)
        {
            if (eval.type is TokenType.any)
            {
                foreach (var member in members)
                {
                    var name = $"{word}.{member.name}";
                    var structWord = (name, member.defaultValue, member.type);
                    // Info(loc, "Adding var {0} of type {1}", name, member.type);
                    RegisterTypedWord(keyword, structWord);
                }
            }
            else
            {
                var memberType = members[0].type;
                Assert(memberType is TokenType.any || memberType.Equals(eval.type), endToken.loc,
                    $"Expected type `{TypeNames(memberType)}` on the stack at the end of the compile-time evaluation, but found: `{TypeNames(eval.type)}`");
                var structWord = (word, eval.operand, memberType);
                RegisterTypedWord(keyword, structWord);
            }
        }
        else
        {
            var frames = result.Select(element => (TypeFrame)element).ToList();
            var expected = members.Select(member => member.type).ToArray();
            TypeChecker.ExpectStackExact(frames, endToken.loc, expected);

            if(!InsideProc) members.Reverse();
            for (int a = 0; a < members.Count; a++)
            {
                var name = $"{word}.{members[a].name}";
                var item = result.ElementAt(InsideProc ? a : result.Count - 1 - a);
                // Info(loc, "Adding var {0} of type {1} and value {2}", name, item.frame.type, item.value);
                var structWord = (name, item.operand, item.type);
                RegisterTypedWord(keyword, structWord);
            }
        }
        structVarsList.Add((word, wordIndex));
    }

    static void RegisterTypedWord(KeywordType keyword, TypedWord structWord)
    {
        if (keyword is KeywordType.colon) constList.Add(structWord);
        else
        {
            if (InsideProc) CurrentProc.localVars.Add(structWord);
            else varList.Add(structWord);
        }
    }

    static bool ParseStruct(string word, Loc loc)
    {
        var members = new List<StructMember>();
        ExpectNextKeyword(loc, KeywordType.colon, "`:` after keyword `struct`");
        while(PeekIRToken() is {} token)
        {
            if(token.type is TokenType.keyword)
            {
                ExpectNextKeyword(loc, KeywordType.end, "`end` after struct declaration");
                structList.Add((word, members));
                return true;
            }

            var foundWord = wordList[ExpectNextToken(loc, TokenType.word, "struct member name").operand];
            if(NextIRToken() is {} nameType)
            {
                var errorText = "Expected struct member type but found";
                if(nameType is (TokenType.word, int typeIndex, _))
                {
                    var foundType = wordList[typeIndex];
                    if(TryGetTypeName(foundType, out StructType structType))
                    {
                        if(structType.members.Count == 1)
                            members.Add(($"{foundWord}", structType.members[0].type));
                        else foreach (var member in structType.members)
                            members.Add(($"{foundWord}.{member.name}", member.type));
                    }
                    else if(TryGetDataPointer(foundType, out TokenType typePtr))
                        members.Add((foundWord, typePtr));
                    else Error(nameType.loc, $"{errorText} the Word: `{foundType}`");
                }
                else if(nameType.type is TokenType.keyword)
                    Error(loc, $"{errorText} the Keyword: `{(KeywordType)nameType.operand}`");
                else Error(nameType.loc, $"{errorText}: `{TypeNames(nameType.type)}`");
            }
        }
        Error(loc, "Expected struct members or `end` after struct declaration");
        return false;
    }

    static bool ParseProcedure(string name, ref Op? result, Op op)
    {
        List<TokenType> ins = new();
        List<TokenType> outs = new();
        var foundArrow = false;

        ExpectNextKeyword(op.loc, KeywordType.colon, "`:` after keyword `proc`");
        var errorText = "Expected proc contract or `:` after procedure definition, but found";
        while(NextIRToken() is {} tok)
        {
            if(tok.type is TokenType.keyword && (KeywordType)tok.operand is {} typ)
            {
                if(typ is KeywordType.arrow)
                {
                    Assert(!foundArrow, tok.loc, "Duplicated `->` found on procedure definition");
                    foundArrow = true;
                }
                else if(typ is KeywordType.colon)
                {
                    result = DefineProc(name, op, new(ins, outs));
                    return true;
                }
                else Error(tok.loc, $"{errorText}: `{typ}`");
            }
            else if(tok.type is TokenType.word)
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
                else if(TryGetDataPointer(foundWord, out TokenType typePtr))
                {
                    if(!foundArrow) ins.Add(typePtr);
                    else outs.Add(typePtr);
                }
                else Error(tok.loc, $"{errorText} the Word: `{foundWord}`");
            }
            else Error(tok.loc, $"{errorText}: `{TypeNames(tok.type)}`");
        }
        Error(op.loc, $"{errorText} nothing");
        return false;
    }
    
    static bool ParseMemory(string word, Loc loc)
    {
        ExpectNextKeyword(loc, KeywordType.colon, "`:` after `mem`");
        var valueToken = ExpectNextToken(loc, TokenType.@int, "memory size after `:`");
        ExpectNextKeyword(loc, KeywordType.end, "`end` after memory size");
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

    static Op ParseBindings(Op op)
    {
        Assert(InsideProc, op.loc, "Bindings cannot be used outside of a procedure");
        var words = new List<string>();
        var proc = CurrentProc;
        while(NextIRToken() is {} tok)
        {
            if(tok.type is TokenType.keyword)
            {
                if((KeywordType)tok.operand is KeywordType.colon)
                {
                    proc.bindings.AddRange(words.Reverse<string>());
                    op.operand = words.Count;
                }
                else Error(tok.loc, $"Expected `:` to close binding definition, but found: {(KeywordType)tok.operand}");
                break;
            }
            else if(tok.type is TokenType.word) words.Add(wordList[tok.operand]);
            else Error(tok.loc, $"Expected only words on binding definition, but found: {TypeNames(tok.type)}");
        }
        return op;
    }

    static Op? ParseCaseMatch(Op op)
    {
        CaseType caseType = CaseType.none;
        List<int> optionMatch = new();
        var proc = CurrentProc;
        int i = 0;
        while (i < IRTokens.Count)
        {
            IRToken token = IRTokenAt(i++);
            if(token.type is TokenType.keyword && (KeywordType)token.operand is KeywordType.@do) break;
            else if(token.type is TokenType.word && wordList[token.operand] is {} word)
            {
                if(word.Equals("_"))
                {
                    caseType = CaseType.@default;
                    continue;
                }
                if(TryParseRange(word, token.loc, out(int start, int end) range))
                {
                    if(caseType is CaseType.none or CaseType.range)
                    {
                        caseType = CaseType.range;
                        optionMatch.Add(range.start);
                        optionMatch.Add(range.end);
                        continue;
                    }
                    if(caseType is CaseType.equal or CaseType.match)
                    {
                        caseType = CaseType.range;
                        var values = optionMatch.ToArray();
                        optionMatch.Clear();
                        for (int j = 0; j < values.Count(); j++)
                        {                    
                            optionMatch.Add(values[j]);
                            optionMatch.Add(values[j]);
                        }
                        optionMatch.Add(range.start);
                        optionMatch.Add(range.end);
                        continue;
                    }
                }
                
                if(TryGetIntrinsic(word, out IntrinsicType intr))
                {
                    Assert(caseType is CaseType.none or CaseType.equal, token.loc,
                        "Case comparison only supports one value at the match option.");
                    caseType = intr switch
                    {
                        IntrinsicType.lesser    => CaseType.lesser,
                        IntrinsicType.lesser_e  => CaseType.lesser_e,
                        IntrinsicType.greater   => CaseType.greater,
                        IntrinsicType.greater_e => CaseType.greater_e,
                        IntrinsicType.and       => CaseType.bit_and,
                        _ => CaseType.none,
                    };

                    if(caseType is CaseType.none)
                        ErrorHere($"IntrinsicType not implemented in `ParseCaseMatch` yet: {intr}");
                    i++;
                    break;
                }

                if(TryGetConstName(word, out TypedWord res) && res.type is TokenType.@int)
                    token = new (res, token.loc);
            }
            
            if(token.type is TokenType.@int)
            {
                if     (caseType is CaseType.none)  caseType = CaseType.equal;
                else if(caseType is CaseType.equal) caseType = CaseType.match;
                else if(caseType is CaseType.range) optionMatch.Add(token.operand);
                else ErrorHere("Not implemented case", token.loc);

                optionMatch.Add(token.operand);
                continue;
            }

            InvalidToken(token, "case declaration");
        }

        NextIRTokens(i-1);
        var current = proc.caseBlocks[proc.currentBlock];
        current.Add((caseType, optionMatch.ToArray()));
        op.operand = current.Count() - 1;
        PushBlock(op);
        return null;
    }

    static IRToken ExpectNextToken(Loc loc, TokenType expected, string notFound)
        => NextIRToken().ExpectToken(loc, expected, notFound);

    static IRToken ExpectToken(this IRToken? iRToken, Loc loc, TokenType expected, string notFound)
    {
        var sb = new StringBuilder();
        var errorLoc = loc;
        if(iRToken is {} token)
        {
            if(token.type.Equals(expected)) return token;

            sb.Append($"Expected {TypeNames(expected)} {notFound}, but found ");
            errorLoc = token.loc;

            if(token.type is TokenType.word && TryGetIntrinsic(wordList[token.operand], out IntrinsicType intrinsic))
                 sb.Append($"the Intrinsic `{intrinsic}`");
            else sb.Append($"a `{TypeNames(token.type)}`");
        }
        else sb.Append($"Expected {notFound}, but found nothing");
        Error(errorLoc, sb.ToString());
        return default;
    }

    static IRToken ExpectKeyword(this IRToken? iRToken, Loc loc, KeywordType expected, string notFound)
        => iRToken.ExpectToken(loc, TokenType.keyword, notFound)
                .ExpectKeyword(loc, expected, notFound);

    static IRToken ExpectNextKeyword(Loc loc, KeywordType expected, string notFound) 
        => ExpectNextToken(loc, TokenType.keyword, notFound)
            .ExpectKeyword(loc, expected, notFound);

    static IRToken ExpectKeyword(this IRToken tok, Loc loc, KeywordType expected, string notFound)
    {
        if(!(expected.HasFlag((KeywordType)tok.operand)))
            Error(tok.loc, $"Expected keyword to be `{expected}`, but found `{(KeywordType)tok.operand}`");
        return tok;
    }
}
