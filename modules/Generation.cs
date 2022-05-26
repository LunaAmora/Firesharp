using System.Diagnostics;

namespace Firesharp;

static partial class Firesharp
{
    static void GenerateWasm(List<Op> program)
    {
        if (Path.GetDirectoryName(filepath) is not string dir)
        {
            Error("could not resolve file directory");
            return;
        }

        string buildPath = Path.Combine(dir, "build");
        Directory.CreateDirectory(buildPath);
        string outPath = Path.Combine(buildPath, "out.wat");

        using (var file     = new FileStream(outPath, FileMode.Create))
        using (var buffered = new BufferedStream(file))
        using (var output   = new StreamWriter(buffered))
        {
            output.WriteLine("(memory 1)");
            output.WriteLine("(export \"memory\" (memory 0))\n");

            output.WriteLine("(global $LOCAL_STACK (mut i32) (i32.const 0))\n");

            output.WriteLine("(func $dup  (param i32 )        (result i32 i32)     local.get 0 local.get 0)");
            output.WriteLine("(func $swap (param i32 i32)     (result i32 i32)     local.get 1 local.get 0)");
            output.WriteLine("(func $over (param i32 i32)     (result i32 i32 i32) local.get 0 local.get 1 local.get 0)");
            output.WriteLine("(func $rot  (param i32 i32 i32) (result i32 i32 i32) local.get 1 local.get 2 local.get 0)\n");

            output.WriteLine("(func $aloc_local (param i32) global.get $LOCAL_STACK local.get 0 i32.add global.set $LOCAL_STACK)");
            output.WriteLine("(func $free_local (param i32) global.get $LOCAL_STACK local.get 0 i32.sub global.set $LOCAL_STACK)");
            output.WriteLine("(func $bind_local (param i32) global.get $LOCAL_STACK local.get 0 i32.store i32.const 4 call $aloc_local)");
            output.WriteLine("(func $push_bind  (param i32) (result i32) global.get $LOCAL_STACK local.get 0 i32.sub i32.load)");

            output.WriteLine("\n(func $start");

            foreach (Op op in program)
            {
                GenerateOp(op, output);
            }

            output.WriteLine(")\n");
            output.WriteLine("(export \"_start\" (func $start))");
        }
        string outWasm = $"{buildPath}/out.wasm";
        CmdEcho("wat2wasm {0} -o {1}", outPath, outWasm);
        // CmdEcho("wasm-opt -Oz --enable-multivalue {0} -o {0}", outWasm);
        // CmdEcho("wasm2wat {0} -o {1}", outWasm, outPath);
        // CmdEcho("wasmtime {0}", outWasm);
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

    static bool TryWriteLine(this StreamWriter writer, (bool sucess, string text) str)
    {
        if (str.sucess)
        {
            writer.WriteLine(str.text);
        }
        else
        {
            Error(str.text);
        }
        return str.sucess;
    }

    static bool GenerateOp(Op op, StreamWriter output) => output.TryWriteLine(op.Type switch
    {
        OpType.push_int  => (true, $"  i32.const {op.Operand}"),
        OpType.push_bool => (true, $"  i32.const {op.Operand}"),
        OpType.push_ptr  => (true, $"  i32.const {op.Operand}"),
        OpType.over      => (true, "  call $over"),
        OpType.swap      => (true, "  call $swap"),
        OpType.dup       => (true, "  call $dup"),
        OpType.rot       => (true, "  call $rot"),
        OpType.drop      => (true, "  drop"),
        OpType.intrinsic => (IntrinsicType)op.Operand switch
        {
            IntrinsicType.plus  => (true, "  i32.add"),
            IntrinsicType.minus => (true, "  i32.sub"),
            IntrinsicType.equal => (true, "  i32.eq"),
            _ => (false, $"Op type not implemented in generation: {op.Type.ToString()}")
        },
        _ => (false, $"Op type not implemented in generation: {op.Type.ToString()}")
    });
}