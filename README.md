# Firesharp

__Firesharp__ is an experimental programming language being developed in C# that is:
- Stack based
- Concatenative
- Compiled to WASM
- Heavily inspired by [Porth](https://gitlab.com/tsoding/porth).

### Quickstart

You will need to have the following:
- [WABT](https://github.com/WebAssembly/wabt)
- [WASMTIME](https://wasmtime.dev/)

Then, you can compile the [test.fire](./src/test.fire) file with:

```console
$ dotnet run -com src/test.fire
... program logs ...
Hello World!
```
