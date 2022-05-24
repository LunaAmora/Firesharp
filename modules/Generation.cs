﻿using System.Diagnostics;

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

        using (FileStream file = new FileStream(outPath, FileMode.Create))
        using (BufferedStream buffered = new BufferedStream(file))
        using (StreamWriter output = new StreamWriter(buffered))
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
                GenerateOp(op, output)();
            }

            output.WriteLine(")\n");
            output.WriteLine("(export \"_start\" (func $start))");
        }
        string outWasm = $"{buildPath}/out.wasm";
        CmdEcho($"wat2wasm {outPath} -o {outWasm}");
        // CmdEcho("wasm-opt -Oz {outWasm} -o {outWasm}");
        // CmdEcho($"wasmtime {outWasm}");
    }

    static void CmdEcho(string toExecute)
    {
        Console.WriteLine($"[CMD] {toExecute}");
        Process cmd = new Process();
        cmd.StartInfo.FileName = "/bin/bash";
        cmd.StartInfo.RedirectStandardInput = true;
        cmd.StartInfo.RedirectStandardOutput = true;
        cmd.StartInfo.CreateNoWindow = true;
        cmd.StartInfo.UseShellExecute = false;
        cmd.Start();

        cmd.StandardInput.WriteLine(toExecute);
        cmd.StandardInput.Flush();
        cmd.StandardInput.Close();
        cmd.WaitForExit();
        Console.Write(cmd.StandardOutput.ReadToEnd());
    }

    static Action GenerateOp(Op op, StreamWriter output) => op.Type switch
    {
        OpType.push_int  => () => output.WriteLine("  i32.const {0}", op.Operand),
        OpType.push_bool => () => output.WriteLine("  i32.const {0}", op.Operand),
        OpType.push_ptr  => () => output.WriteLine("  i32.const {0}", op.Operand),
        OpType.over      => () => output.WriteLine("  call $over"),
        OpType.swap      => () => output.WriteLine("  call $swap"),
        OpType.dup       => () => output.WriteLine("  call $dup"),
        OpType.rot       => () => output.WriteLine("  call $rot"),
        OpType.drop      => () => output.WriteLine("  drop"),
        OpType.intrinsic => () => ((IntrinsicType)op.Operand switch
        {
            IntrinsicType.plus  => () => output.WriteLine("  i32.add"),
            IntrinsicType.minus => () => output.WriteLine("  i32.sub"),
            IntrinsicType.equal => () => output.WriteLine("  i32.eq"),
            _ => (Action) (() => Error($"intrinsic value `{(IntrinsicType)op.Operand}`is not valid or is not implemented"))
        })(),

        _ => () => Error($"Op type not implemented in generation: {op.Type.ToString()}")
    };
}