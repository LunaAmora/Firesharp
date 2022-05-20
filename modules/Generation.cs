using System.Diagnostics;

namespace Firesharp;

static partial class Firesharp
{
    static void GenerateWasm(Queue<Op> program)
    {
        using (output = new StreamWriter("out.wat", false))
        {
            output.WriteLine("(func $start");

            while (program.TryDequeue(out var current))
            {
                current.Generate();
            }

            output.WriteLine(")");

            output.WriteLine("(export \"start\" (func $start))");
        }

        CmdEcho("wat2wasm out.wat -o out.wasm");
        CmdEcho("wasm-opt -Oz out.wasm -o out.wasm");
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

    static Action GenerateOp<opType>(Op<opType> op)
        where opType : struct, Enum => op.Type switch
    {
        ActionType.push_int  => () =>
        {
            output.WriteLine($"  i32.const {op.Operand}");
        },
        ActionType.push_bool => () =>
        {
            output.WriteLine($"  i32.const {op.Operand}");
        },
        ActionType.push_ptr  => () =>
        {
            output.WriteLine($"  i32.const {op.Operand}");
        },
        // ActionType.push_str  => () =>
        // {

        // },
        // ActionType.push_cstr => () =>
        // {

        // },
        ActionType.drop => () =>
        {
            output.WriteLine("  drop");
        },
        IntrinsicType.plus => () =>
        {
            output.WriteLine("  i32.add");
        },
        IntrinsicType.minus => () =>
        {
            output.WriteLine("  i32.sub");
        },
        IntrinsicType.equal => () =>
        {
            output.WriteLine("  i32.eq");
        },
        IntrinsicType.dump => () =>
        {
            output.WriteLine("  i32.drop");
        },
        // IntrinsicType.call => () =>
        // {

        // },
        _ => () => Debug.Assert(false, $"[Error] Op type not implemented in generation: {op.Type.ToString()}")
    };
}