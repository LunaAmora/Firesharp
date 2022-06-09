﻿using CliWrap;

namespace Firesharp;

using static TypeChecker;
using static Tokenizer;
using static Parser;

static class Generator
{
    public static async Task GenerateWasm(List<Op> program)
    {
        if (Path.GetDirectoryName(Filepath) is not string dir)
        {
            Error("Could not resolve file directory");
            return;
        }

        string buildPath = Path.Combine(dir, "build");
        Directory.CreateDirectory(buildPath);
        string outPath = Path.Combine(buildPath, "out.wat");

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
            output.WriteLine("(func $push_local (param i32) (result i32) global.get $LOCAL_STACK local.get 0 i32.sub)\n");
            
            program.ForEach(op => output.TryWriteLine(GenerateOp(op)));

            output.WriteLine("(export \"_start\" (func $start))\n");

            if (dataList.Count > 0 || varList.Count > 0)
            {
                output.WriteLine("(data (i32.const 0)");
                dataList.ForEach(data => output.WriteLine("  \"{0}\"", data.name));

                var padding = 4 - (totalDataSize % 4);
                if(padding < 4) output.WriteLine("  \"{0}\"", new String('0', padding).Replace("0", "\\00"));

                for (int i = 0; i < varList.Count; i++)
                {
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
        string outWasm = $"{buildPath}/out.wasm";

        await CmdEcho("wat2wasm", outPath, "-o", outWasm);
        // await CmdEcho("wasm-opt", "-Oz", "--enable-multivalue", outWasm, "-o", outWasm);
        // await CmdEcho("wasm2wat", outWasm, "-o", outPath);
        await CmdEcho("wasmtime", outWasm);
    }

    static async Task CmdEcho(string target, params string[] arg)
    {
        var cmd = Cli.Wrap(target)
            .WithValidation(CommandResultValidation.None)
            .WithArguments(arg) |
            (FConsole.Output.WriteLine, FConsole.Error.WriteLine);
        WritePrefix("[CMD] ", cmd.ToString());
        var result = await cmd.ExecuteAsync();
        Assert(result.ExitCode == 0, "External command error, please report this in the project's github!");
    }
    
    static void TryWriteLine(this StreamWriter writer, string text)
    {
        if (!string.IsNullOrEmpty(text)) writer.WriteLine(text);
    }

    static string GenerateOp(Op op) => op.type switch
    {
        OpType.push_global_mem => $"  i32.const {finalDataSize + totalMemSize + op.operand}",
        OpType.push_local_mem  => $"  i32.const {(CurrentProc.bindCount + 1) * 4 + op.operand} call $push_local",
        OpType.push_global  => $"  i32.const {finalDataSize + op.operand * 4}",
        OpType.push_local  => $"  i32.const {(CurrentProc.bindCount + 1 + op.operand) * 4 + CurrentProc.procMemSize} call $push_local",
        OpType.push_str  => $"  i32.const {dataList[op.operand].size}\n  i32.const {dataList[op.operand].offset}",
        OpType.push_int  or
        OpType.push_ptr  or
        OpType.push_bool => $"  i32.const {op.operand}",
        OpType.over      => "  call $over",
        OpType.swap      => "  call $swap",
        OpType.dup       => "  call $dup",
        OpType.rot       => "  call $rot",
        OpType.drop      => "  drop",
        OpType.call      => $"  call ${procList[op.operand].name}",
        OpType.prep_proc => "(func $".AppendProc(op),
        OpType.equal     => "  i32.eq",
        OpType.if_start  => "  if".AppendContract(op),
        OpType._else     => "  else",
        OpType.end_if    or 
        OpType.end_else  => "  end",
        OpType.end_proc  => ")\n".PrependProc(op),
        OpType.bind_stack => BindValues(op.operand),
        OpType.push_bind  => $"  i32.const {(op.operand + 1) * 4} call $push_local i32.load",
        OpType.pop_bind   => PopBind(op.operand),
        OpType.intrinsic => (IntrinsicType)op.operand switch
        {
            IntrinsicType.plus      => "  i32.add",
            IntrinsicType.minus     => "  i32.sub",
            IntrinsicType.load32    => "  i32.load",
            IntrinsicType.store32   => "  call $swap\n  i32.store",
            IntrinsicType.fd_write  => "  call $fd_write",
            IntrinsicType.cast_ptr  or
            IntrinsicType.cast_int  or
            IntrinsicType.cast_bool => string.Empty,
            _ => Error(op.loc, $"Intrinsic type not implemented in `GenerateOp` yet: `{(IntrinsicType)op.operand}`")
        },
        _ => Error(op.loc, $"Op type not implemented in `GenerateOp` yet: {op.type}")
    };

    static string BindValues(int bindNumber)
    {
        var sb = new StringBuilder();

        if(bindNumber > 0) sb.Append(" ");
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

    static string PrependProc(this string str, Op op)
    {
        Assert(InsideProc, "Unreachable, parser error.");
        var proc = CurrentProc;
        if(proc.procMemSize + proc.localVars.Count > 0)
        {
            str = $"  i32.const {proc.procMemSize + (proc.localVars.Count * 4)} call $free_local\n{str}";
        }
        ExitCurrentProc();
        return str;
    }

    static string AppendProc(this string str, Op op)
    {
        Assert(!InsideProc, "Unreachable, parser error.");
        var proc = procList[op.operand];
        CurrentProc = proc;
        var count = proc.localVars.Count;
        
        StringBuilder sb = new StringBuilder(str);
        if(proc is var (name, (ins, outs)))
        {
            (int ins, int outs) contr = (ins.Count, outs.Count);
            sb.Append(name);
            sb.AppendContract(contr);

            if(proc.procMemSize + count > 0)
            {
                sb.Append($"\n  i32.const {proc.procMemSize + (count * 4)} call $aloc_local");
            }

            for (int a = 0; a < count; a++)
            {
                var value = proc.localVars[a].value;
                if(value != 0)
                {
                    var offset = proc.procMemSize + (a + 1) * 4;
                    sb.Append($"\n  i32.const {offset} call $push_local i32.const {value} i32.store");
                }
            }
            
            if(contr.ins > 0) sb.Append("\n ");
            for (int i = 0; i < contr.ins; i++) sb.Append($" local.get {i}");
        }
        
        return sb.ToString();
    }

    static string AppendContract(this string str, Op op)
    {
        if(blockContacts.ContainsKey(op) && blockContacts[op] is {} contract)
        {
            return new StringBuilder(str).AppendContract(contract).ToString();
        }
        return str;
    }

    static StringBuilder AppendContract(this StringBuilder sb, (int ins, int outs) contract)
    {
        if (contract.ins > 0)
        {
            sb.Append(" (param");
            sb.Insert(sb.Length, " i32", contract.ins);
            sb.Append(")");
        }
        if (contract.outs > 0)
        {
            sb.Append(" (result");
            sb.Insert(sb.Length, " i32", contract.outs);
            sb.Append(")");
        }
        return sb;
    }
}
