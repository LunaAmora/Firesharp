# Firesharp

__Firesharp__ is an experimental programming language being developed in C# that is:
- Stack based
- Concatenative
- Compiled to WASM
- Heavily inspired by [Porth](https://gitlab.com/tsoding/porth).

### Quickstart (only functional on linux yet)

You will need [WABT](https://github.com/WebAssembly/wabt). Then, you can compile the `test.fire` file with:

```console
$ dotnet run -com src/test.fire
```
