using System.Diagnostics;

namespace Firesharp;

static partial class Firesharp
{
    static void GenerateWasm(List<Op> program)
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
        CmdEcho("wat2wasm {0} -o {1}", outPath, outWasm);
        // CmdEcho("wasm-opt -Oz --enable-multivalue {0} -o {0}", outWasm);
        // CmdEcho("wasm2wat {0} -o {1}", outWasm, outPath);
        CmdEcho("wasmtime {0}", outWasm);
    }

    static void CmdEcho(string format, params object?[] arg)
    {
        Console.Write($"[CMD] ");
        Console.WriteLine(format, arg);
        var cmd = new Process();
        cmd.StartInfo.FileName = "/bin/bash";
        cmd.StartInfo.RedirectStandardInput = true;
        cmd.StartInfo.RedirectStandardOutput = true;
        cmd.StartInfo.CreateNoWindow = true;
        cmd.StartInfo.UseShellExecute = false;
        cmd.Start();

        cmd.StandardInput.WriteLine(format, arg);
        cmd.StandardInput.Flush();
        cmd.StandardInput.Close();
        cmd.WaitForExit();
        Console.Write(cmd.StandardOutput.ReadToEnd());
    }

    static void TryWriteLine(this StreamWriter writer, string text)
    {
        if (!string.IsNullOrEmpty(text)) writer.WriteLine(text);
    }

    static string GenerateOp(Op op) => op.type switch
    {
        OpType.push_global_mem => $"  i32.const {finalDataSize + op.operand}",
        OpType.push_local_mem => $"  global.get $LOCAL_STACK i32.const {op.operand + 4} i32.sub",
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
        OpType.if_start  => "  if".AppendContract(op),
        OpType._else     => "  else",
        OpType.end_if    or 
        OpType.end_else  => "  end",
        OpType.end_proc  => ")\n".PrependProc(op),
        OpType.intrinsic => (IntrinsicType)op.operand switch
        {
            IntrinsicType.plus      => "  i32.add",
            IntrinsicType.minus     => "  i32.sub",
            IntrinsicType.equal     => "  i32.eq",
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
        var proc = procList[op.operand];
        if(proc.procMemSize > 0)
        {
            str = $"  i32.const {proc.procMemSize} call $free_local\n{str}";
        }
        return str;
    }

    static string AppendProc(this string str, Op op)
    {
        var proc = procList[op.operand];
        var sb = new StringBuilder($"{str}{proc.name}");
        if(proc.contract is Contract contract)
        {
            (int ins, int outs) contr = (contract.ins.Count, contract.outs.Count);
            AppendContract(sb, contr);
            if(contr.ins > 0) sb.Append("\n ");
            for (int i = 0; i < contr.ins; i++) sb.Append($" local.get {i}");
        }
        
        if(proc.procMemSize > 0) sb.Append($"\n  i32.const {proc.procMemSize} call $aloc_local");

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
