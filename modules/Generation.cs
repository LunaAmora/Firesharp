using System.Diagnostics;
using CliWrap;

namespace Firesharp;

static partial class Firesharp
{
    static int finalDataSize => ((totalDataSize + 3)/4)*4;

    public static async Task GenerateWasm(List<Op> program)
    {
        if (Path.GetDirectoryName(filepath) is not string dir)
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

            output.WriteLine("(global $LOCAL_STACK (mut i32) (i32.const {0}))\n", finalDataSize + totalMemSize);

            output.WriteLine("(func $dup  (param i32 )        (result i32 i32)     local.get 0 local.get 0)");
            output.WriteLine("(func $swap (param i32 i32)     (result i32 i32)     local.get 1 local.get 0)");
            output.WriteLine("(func $over (param i32 i32)     (result i32 i32 i32) local.get 0 local.get 1 local.get 0)");
            output.WriteLine("(func $rot  (param i32 i32 i32) (result i32 i32 i32) local.get 1 local.get 2 local.get 0)\n");

            output.WriteLine("(func $aloc_local (param i32) global.get $LOCAL_STACK local.get 0 i32.add global.set $LOCAL_STACK)");
            output.WriteLine("(func $free_local (param i32) global.get $LOCAL_STACK local.get 0 i32.sub global.set $LOCAL_STACK)");
            output.WriteLine("(func $bind_local (param i32) global.get $LOCAL_STACK local.get 0 i32.store i32.const 4 call $aloc_local)");
            output.WriteLine("(func $push_bind  (param i32) (result i32) global.get $LOCAL_STACK local.get 0 i32.sub i32.load)\n");

            varList.ForEach(vari => output.WriteLine($"(global ${vari.name} (mut i32) (i32.const {vari.value}))\n"));

            program.ForEach(op => output.TryWriteLine(GenerateOp(op)));

            output.WriteLine("(export \"_start\" (func $start))\n");

            if (dataList.Count > 0)
            {
                output.WriteLine("(data (i32.const 0)");
                dataList.ForEach(data => output.WriteLine("  \"{0}\"", data.name));
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
        Debug.Assert(_console is {});
        var cmd = Cli.Wrap(target)
            .WithValidation(CommandResultValidation.None)
            .WithArguments(arg) |
            (_console.Output.WriteLine, _console.Error.WriteLine);
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
        OpType.load_local      => $"  local.get ${currentProc?.localVars[op.operand].name}",
        OpType.store_local     => $"  local.set ${currentProc?.localVars[op.operand].name}",
        OpType.load_global     => $"  global.get ${varList[op.operand].name}",
        OpType.store_global    => $"  global.set ${varList[op.operand].name}",
        OpType.push_global_mem => $"  i32.const {finalDataSize + op.operand}",
        OpType.push_local_mem  => $"  global.get $LOCAL_STACK i32.const {op.operand + 4} i32.sub",
        OpType.push_str  => $"  i32.const {dataList[op.operand].size}\n  i32.const {dataList[op.operand].offset}",
        OpType.push_int  => $"  i32.const {op.operand}",
        OpType.push_ptr  => $"  i32.const {op.operand}",
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
        OpType.intrinsic => (IntrinsicType)op.operand switch
        {
            IntrinsicType.plus      => "  i32.add",
            IntrinsicType.minus     => "  i32.sub",
            IntrinsicType.load32    => "  i32.load",
            IntrinsicType.store32   => "  call $swap\n  i32.store",
            IntrinsicType.fd_write  => "  call $fd_write",
            IntrinsicType.cast_ptr  => string.Empty,
            IntrinsicType.cast_bool => string.Empty,
            _ => Error(op.loc, $"Intrinsic type not implemented in `GenerateOp` yet: `{(IntrinsicType)op.operand}`")
        },
        _ => Error(op.loc, $"Op type not implemented in `GenerateOp` yet: {op.type}")
    };

    static string PrependProc(this string str, Op op)
    {
        currentProc = procList[op.operand];
        if(currentProc.procMemSize > 0)
        {
            str = $"  i32.const {currentProc.procMemSize} call $free_local\n{str}";
        }
        currentProc = null;
        return str;
    }

    static string AppendProc(this string str, Op op)
    {
        currentProc = procList[op.operand];
        StringBuilder sb = new StringBuilder(str);
        if(currentProc is {} proc && proc is var (name, (ins, outs)))
        {
            (int ins, int outs) contr = (ins.Count, outs.Count);
            sb.Append(name);
            AppendContract(sb, contr);

            proc.localVars.ForEach(vari => sb.Append($"\n  (local ${vari.name} i32)"));
            proc.localVars
                .FindAll(vari => vari.value != 0)
                .ForEach(vari => sb.Append($"\n  (local.set ${vari.name} (i32.const {vari.value}))"));
            
            if(contr.ins > 0) sb.Append("\n ");
            for (int i = 0; i < contr.ins; i++) sb.Append($" local.get {i}");
        }
        
        if(currentProc.procMemSize > 0)
        {
            sb.Append($"\n  i32.const {currentProc.procMemSize} call $aloc_local");
        }

        return sb.ToString();
    }

    static string AppendContract(this string str, Op op)
    {
        if(blockContacts.ContainsKey(op) && blockContacts[op] is (int ins, int outs) contract)
        {
            var sb = new StringBuilder(str);
            return AppendContract(sb, contract).ToString();
        }
        return str;
    }

    static StringBuilder AppendContract(StringBuilder sb, (int ins, int outs) contract)
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
