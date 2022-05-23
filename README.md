# Firesharp

An experiment on programming language development using C#, that is:
- Stack based
- Concatenative
- Compiled to WASM

### Quickstart (only functional on linux yet)

You will need [WABT](https://github.com/WebAssembly/wabt). Then, you can compile the `test.fire` file with:

```console
$ dotnet run -com src/test.fire
```
