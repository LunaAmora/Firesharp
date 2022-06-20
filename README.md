# Firesharp

__Firesharp__ is an experimental programming language being developed in C# that is:

- Stack based
- Concatenative
- Compiled to WASM
- Heavily inspired by [Porth](https://gitlab.com/tsoding/porth).

## Quickstart

You will need to have the following:

- [WABT](https://github.com/WebAssembly/wabt)
- [WASMTIME](https://wasmtime.dev/)

Then, you can compile the [test.fire](./src/test.fire) file with:

```console
$ dotnet run -com src/test.fire
... program logs ...
number is less than 0
number is 0 or nice
number is 1 or more than 7 and not nice
number is between 2 and 6
number is between 2 and 6
number is between 2 and 6
number is 7
number is 0 or nice
number is 0 or nice
number is 1 or more than 7 and not nice
```
