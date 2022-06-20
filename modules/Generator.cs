namespace Firesharp;

using static Parser;

static class Generator
{
    public static void GenerateWasm(List<Op> program, string outPath)
    {
        Info(text: "Generating {0}", arg: outPath);
        using (var file     = new FileStream(outPath, FileMode.Create))
        using (var buffered = new BufferedStream(file))
        using (var output   = new StreamWriter(buffered))
        {
            output.WriteLine("(import \"wasi_unstable\" \"fd_write\" (func $fd_write (param i32 i32 i32 i32) (result i32)))");
            output.WriteLine("(memory 1)");
            output.WriteLine("(export \"memory\" (memory 0))\n");

            output.WriteLine("(global $LOCAL_STACK (mut i32) (i32.const {0}))\n", finalDataSize + totalMemSize + totalVarsSize);

            output.WriteLine("(func $dup  (param i32 )        (result i32 i32)     local.get 0 local.get 0)");
            output.WriteLine("(func $swap (param i32 i32)     (result i32 i32)     local.get 1 local.get 0)");
            output.WriteLine("(func $over (param i32 i32)     (result i32 i32 i32) local.get 0 local.get 1 local.get 0)");
            output.WriteLine("(func $rot  (param i32 i32 i32) (result i32 i32 i32) local.get 1 local.get 2 local.get 0)\n");

            output.WriteLine("(func $aloc_local (param i32) global.get $LOCAL_STACK local.get 0 i32.add global.set $LOCAL_STACK)");
            output.WriteLine("(func $free_local (param i32) global.get $LOCAL_STACK local.get 0 i32.sub global.set $LOCAL_STACK)");
            output.WriteLine("(func $bind_local (param i32) global.get $LOCAL_STACK local.get 0 i32.store i32.const 4 call $aloc_local)");
            output.WriteLine("(func $push_local (param i32) (result i32) global.get $LOCAL_STACK local.get 0 i32.sub)");
            
            program.ForEach(op => output.TryWriteLine(GenerateOp(op), $"{op.type}, operand: {op.operand}"));

            output.WriteLine("\n(export \"_start\" (func $start))\n");

            if(dataList.Count > 0 || varList.Count > 0)
            {
                output.WriteLine("(data (i32.const 0)");
                dataList.Where(data => data.offset >= 0)
                        .OrderBy(data => data.offset).ToList()
                        .ForEach(data => output.WriteLine("  \"{0}\"", data.name));

                var padding = 4 - (totalDataSize % 4);
                if(padding < 4) output.WriteLine("  \"{0}\"", new String('0', padding).Replace("0", "\\00"));

                for (int i = 0; i < varList.Count; i++)
                {
                    // Info(default, "Generating {2} data: {0}, Value {1}", varList[i].name, varList[i].value, varList[i].type);
                    var hex = varList[i].value.ToString("X");
                    if(hex.Length%2 != 0) hex = hex.PadLeft(hex.Length +1, '0');
                    hex = hex.PadRight(8, '0');
                    hex = string.Join("\\", Enumerable
                        .Range(0, hex.Length/2)
                        .Select(i => hex.Substring(i*2, 2)));
                    output.WriteLine("  \"\\{0}\"", hex);
                }
                
                output.WriteLine(")");
            }
        }
    }
    
    static void TryWriteLine(this StreamWriter writer, string text, string comment)
    {
        if(!string.IsNullOrEmpty(text))
        {
            if(debug) writer.WriteLine($"{text} ;; {comment}");
            else writer.WriteLine(text);
        }
    }

    static string GenerateOp(Op op) => op.type switch
    {
        OpType.push_global_mem => $"  i32.const {finalDataSize + totalMemSize + op.operand}",
        OpType.push_local_mem => $"  i32.const {CurrentProc.bindCount * 4 + op.operand} call $push_local",
        OpType.push_global => $"  i32.const {finalDataSize + op.operand * 4}",
        OpType.push_local  => $"  i32.const {(CurrentProc.bindCount + 1 + op.operand) * 4 + CurrentProc.procMemSize} call $push_local",
        OpType.offset_load => $"  i32.const {op.operand} i32.add i32.load",
        OpType.offset      => $"  i32.const {op.operand} i32.add",
        OpType.push_str  => $"  i32.const {dataList[op.operand].size} i32.const {dataList[op.operand].offset}",
        OpType.push_int  or
        OpType.push_ptr  or
        OpType.push_bool => $"  i32.const {op.operand}",
        OpType.over      => "  call $over",
        OpType.swap      => "  call $swap",
        OpType.dup       => "  call $dup",
        OpType.rot       => "  call $rot",
        OpType.drop      => "  drop",
        OpType.unpack    => UnpackStruct(op.operand),
        OpType.call      => $"  call ${procList[op.operand].name}",
        OpType.prep_proc => $"\n(func ${PrepProc(op.operand)}",
        OpType.equal     => "  i32.eq",
        OpType.if_start  => $"  if{BlockContract(op)}",
        OpType.@else     => "  else",
        OpType.end_if    or 
        OpType.end_else  => "  end",
        OpType.@while    => $"  loop $while|{op.operand}{BlockContract(op)}",
        OpType.@do       => $"  if{BlockContract(op)}",
        OpType.end_while => $"  br $while|{op.operand} end end",
        OpType.end_proc  => EndProc(op),
        OpType.bind_stack => BindValues(op.operand),
        OpType.push_bind  => $"  i32.const {(op.operand + 1) * 4} call $push_local i32.load",
        OpType.pop_bind   => PopBind(op.operand),
        OpType.case_start => StartCase(op),
        OpType.case_option => CaseOption(op.operand),
        OpType.end_case    => EndCase(),
        OpType.intrinsic => (IntrinsicType)op.operand switch
        {
            IntrinsicType.plus      => "  i32.add",
            IntrinsicType.minus     => "  i32.sub",
            IntrinsicType.or        => "  i32.or",
            IntrinsicType.and       => "  i32.and",
            IntrinsicType.greater   => "  i32.gt_s",
            IntrinsicType.greater_e => "  i32.ge_s",
            IntrinsicType.lesser    => "  i32.lt_s",
            IntrinsicType.lesser_e  => "  i32.le_s",
            IntrinsicType.load8     => "  i32.load8_s",
            IntrinsicType.store8    => "  call $swap i32.store8",
            IntrinsicType.load16    => "  i32.load16_s",
            IntrinsicType.store16   => "  call $swap i32.store16",
            IntrinsicType.load32    => "  i32.load",
            IntrinsicType.store32   => "  call $swap i32.store",
            IntrinsicType.fd_write  => "  call $fd_write",
            {} cast when cast >= IntrinsicType.cast => string.Empty,
            _ => ErrorHere($"Intrinsic type not implemented in `GenerateOp` yet: `{(IntrinsicType)op.operand}`", op.loc)
        },
        OpType.expectType => string.Empty,
        _ => ErrorHere($"Op type not implemented in `GenerateOp` yet: {op.type}", op.loc)
    };
    
    static string StartCase(Op op)
    {
        var operand = op.operand;
        var proc = CurrentProc;
        proc.currentBlock = operand;
        var block = proc.caseBlocks[operand];
        var sb = new StringBuilder($"  block $case|{operand}{BlockContract(op)}");

        sb.Append($"\n  block $default|0 (param i32)");

        for (int i = block.Count -2 ; i >= 0; i--)
            sb.Append($"\n  block $case{i}|0 (param i32)");

        sb.Append("\n  call $dup");

        for (int i = 0; i < block.Count; i++)
        {
            var match = block[i];
            sb.Append($"\n  {GenerateMatch(match, operand)}");
            if(match.type is not CaseType.@default)
                sb.Append($"\n  br_if $case{i}|{operand} call $dup");
        }

        return sb.ToString();
    }

    static string GenerateMatch(CaseOption option, int operand) => (option.type switch
    {
        CaseType.lesser => $"i32.const {option.value[0]} i32.lt_s",
        CaseType.equal  => $"i32.const {option.value[0]} i32.eq",
        CaseType.match  => MatchMultiValues(option),
        CaseType.range  => MatchRanges(option),
        CaseType.@default =>  $"drop br $default|{operand} end",
        _ => ErrorHere($"CaseType not implemented in `GenerateMatch` yet: {option.type}")
    });

    static string MatchRanges(CaseOption option)
    {
        var count = option.value.Count();
        var sb = new StringBuilder();
        (int start, int end) range;

        if(count <= 2)
        {
            range = (option.value[0], option.value[1]);
            sb.Append("call $dup");
            if(range.start != range.end)
            {
                sb.Append($" i32.const {range.start} i32.ge_s");
                sb.Append("\n  call $over");
                sb.Append($" i32.const {range.end} i32.le_s");
                sb.Append(" i32.and");
            }
            else sb.Append($" i32.const {range.start} i32.eq");
            return sb.ToString();
        }
        
        sb.Append("call $bind_local");
        for (int i = 0; i < count - 1; i += 2)
        {
            range = (option.value[i], option.value[i+1]);
            sb.Append($"\n  i32.const 4 call $push_local i32.load");

            if(range.start != range.end)
            {
                sb.Append($"\n  call $dup  i32.const {range.start} i32.ge_s");
                sb.Append($"\n  call $swap i32.const {range.end} i32.le_s");
                sb.Append(" i32.and");
            }
            else sb.Append($"\n  i32.const {range.start} i32.eq");

            if(i is not 0) sb.Append(" i32.or");
        }
        sb.Append("\n  i32.const 4 call $free_local");
        return sb.ToString();
    }

    static string MatchMultiValues(CaseOption option)
    {
        var count = option.value.Count();

        var sb = new StringBuilder();
        if(count > 2)
        {
            sb.Append("call $bind_local");
            option.value.ToList().ForEach(match =>
            {
                sb.Append($"\n  i32.const 4 call $push_local i32.load");
                sb.Append($" i32.const {match} i32.eq");
            });
            for (int i = 0; i < count-1; i++) sb.Append(" i32.or");
            sb.Append("\n  i32.const 4 call $free_local");
        }
        else
        {
            sb.Append("call $dup");
            sb.Append( $" i32.const {option.value[0]} i32.eq");
            sb.Append("\n  call $over");
            sb.Append( $" i32.const {option.value[1]} i32.eq");
            sb.Append(" i32.or");
        }
        return sb.ToString();
    }

    static string CaseOption(int operand)
    {
        if(operand is 0) return string.Empty;
        return EndCase();
    }

    static string EndCase() => $"  br $case|{CurrentProc.currentBlock} end";

    static string UnpackStruct(int operand)
    {
        var stk = structList[operand];
        var count = stk.members.Count;
        var sb = new StringBuilder();
        if(count > 2)
        {
            sb.Append("  call $bind_local\n");
            var offset = 0;
            stk.members.ForEach(member =>
            {
                sb.Append($"    i32.const 4 call $push_local i32.load");
                sb.Append($"    i32.const {4 * offset++} i32.add i32.load\n");
            });
            sb.Append("  i32.const 4 call $free_local");
        }
        else if(count == 2)
        {
            sb.Append("  call $dup i32.load call $swap");
            sb.Append("  i32.const 4 i32.add i32.load");
        }
        else
        {
            sb.Append("  i32.load");
        }
        return sb.ToString();
    }

    static string BindValues(int bindNumber)
    {
        if(bindNumber <= 0) return string.Empty;
        
        var sb = new StringBuilder(" ");
        for (int i = 0; i < bindNumber; i++)
        {
            sb.Append(" call $bind_local");
        }
        CurrentProc.bindCount += bindNumber;
        return sb.ToString();
    }

    static string PopBind(int bindNumber)
    {
        CurrentProc.bindCount -= bindNumber;
        return $"  i32.const {bindNumber * 4} call $free_local";
    }

    static string EndProc(Op op)
    {
        var proc = CurrentProc;
        ExitCurrentProc();
        if(proc.procMemSize + proc.localVars.Count > 0)
        {
            return $"  i32.const {proc.procMemSize + (proc.localVars.Count * 4)} call $free_local\n)";
        }
        return ")";
    }

    static string PrepProc(int index)
    {
        var proc = procList[index];
        CurrentProc = proc;
        var count = proc.localVars.Count;
        
        StringBuilder sb = new StringBuilder();
        if(proc is var (name, (ins, outs)))
        {
            (int ins, int outs) contr = (ins.Count, outs.Count);
            sb.Append(name);
            sb.Append(BlockContract(contr));

            if(proc.procMemSize + count > 0)
            {
                sb.Append($"\n  i32.const {proc.procMemSize + (count * 4)} call $aloc_local");
            }

            for (int a = 0; a < count; a++)
            {
                if(proc.localVars[a].value is not 0 and int value)
                {
                    var offset = proc.procMemSize + (a + 1) * 4;
                    sb.Append($"\n  i32.const {offset} call $push_local i32.const {value} i32.store");
                    if(debug) sb.Append($" ;; initialize_local, operand: {a}");
                }
            }
            
            if(contr.ins > 0 || (count > 0 && debug)) sb.Append("\n ");
            for (int i = 0; i < contr.ins; i++) sb.Append($" local.get {i}");
        }
        return sb.ToString();
    }

    static string BlockContract(Op op)
    {
        if(blockContacts.ContainsKey(op) && blockContacts[op] is {} contract)
        {
            return BlockContract(contract);
        }
        return string.Empty;
    }

    static string BlockContract((int ins, int outs) contract)
    {
        var sb = new StringBuilder();
        if(contract.ins > 0)
        {
            sb.Append(" (param");
            sb.Insert(sb.Length, " i32", contract.ins);
            sb.Append(")");
        }
        if(contract.outs > 0)
        {
            sb.Append(" (result");
            sb.Insert(sb.Length, " i32", contract.outs);
            sb.Append(")");
        }
        return sb.ToString();
    }
}
