# Firesharp

__Firesharp__ is compiler for __Firelang__, an [Concatenative](https://en.wikipedia.org/wiki/Concatenative_programming_language) [Stack-oriented](https://en.wikipedia.org/wiki/Stack-oriented_programming) programming language that compiles to [WASM](https://webassembly.org/).

All thanks to [Tsoding](https://github.com/rexim), as this project is Heavily inspired by [Porth](https://gitlab.com/tsoding/porth).

## Quickstart

You will need to have the following:

- [WABT](https://github.com/WebAssembly/wabt)
- [WASMTIME](https://wasmtime.dev/) (or any other  WASM runtime)

Then, you can compile the [test.fire](./src/test.fire) file with:

```console
$ dotnet publish
$ ./Firesharp -com src/test.fire -r
... program logs ...
number is less than 0
number is 0
number is 1 or more than 7 and not nice
number is between 2 and 6
number is between 2 and 6
number is between 2 and 6
number is 7
number is nice
number is nice
number is 1 or more than 7 and not nice
```

### Running options and commands

```console
$ Firesharp [options]
$ Firesharp [command] [...]

OPTIONS:
  -h|--help         Shows help text. 
  --version         Shows version information. 
COMMANDS:
    -com <inputfile> [options] Compile a `.fire` file to WebAssembly.
        -r|--run          Run the `.wasm` file with a runtime.
        -d|--debug        Add OpTypes information to output `.wat` file.
        -w|--wat          Decompile the `.wasm` file back into the `.wat`.
        -s|--silent       Don't print any info about compilation phases.
        -g|--graph        Generate a `.svg` call graph of the compiled program. (Needs Graphviz)
        -p|--opt          Optimize the `.wasm` file to reduce it's size. (Needs Binaryen)
        -t|--runtime      Sets the runtime to be used by the `-r` option. Default: "wasmtime".
        -h|--help         Shows help text. 
```
