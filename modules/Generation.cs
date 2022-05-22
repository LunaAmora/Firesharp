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

        using (FileStream file = new FileStream(outPath, FileMode.Create))
        using (BufferedStream buffered = new BufferedStream(file))
        using (StreamWriter output = new StreamWriter(buffered))
        {
            output.WriteLine("(import \"env\" \"memory\" (memory 1))");
            output.WriteLine("(func $start");

            foreach (Op op in program)
            {
                GenerateOp(op, output)();
            }

            output.WriteLine(")");
            output.WriteLine("(export \"start\" (func $start))");
        }

        CmdEcho($"wat2wasm {outPath} -o {buildPath}/out.wasm");
        // CmdEcho("wasm-opt -Oz out/out.wasm -o out/out.wasm");
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
        OpType.push_int  => () =>
        {
            output.WriteLine($"  i32.const {op.Operand}");
        },
        OpType.push_bool => () =>
        {
            output.WriteLine($"  i32.const {op.Operand}");
        },
        OpType.push_ptr  => () =>
        {
            output.WriteLine($"  i32.const {op.Operand}");
        },
        OpType.drop => () =>
        {
            output.WriteLine("  drop");
        },
        OpType.intrinsic => () => ((IntrinsicType)op.Operand switch
        {
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
            _ => (Action) (() => Error($"intrinsic value `{(IntrinsicType)op.Operand}`is not valid or is not implemented"))
        })(),

        _ => () => Error($"Op type not implemented in generation: {op.Type.ToString()}")
    };
}