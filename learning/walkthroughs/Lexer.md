# Lexer.fs walkthrough

A section-by-section tour of `RPGCombatDSL/Lexer.fs` with F#-specific syntax called out as it appears.

## Module declaration

```fsharp
module Lexer
```

Every `.fs` file starts with either `module Name` or `namespace Name`. With `module`, everything in the file is in the `Lexer` module, accessed as `Lexer.tokenize` from elsewhere. There are no `namespace { }` braces — indentation and the top-of-file declaration do the work.

## The `Token` type

```fsharp
type Token =
    | TIdent of string
    | TAttacks
    | TDefends
    ...
    | TEof
```

This is a **discriminated union** (DU) — F#'s answer to "this value is one of these N shapes." Each `|` is a case. `TAttacks` is a bare case (just a tag). `TIdent of string` is a case that carries a payload — to build one you write `TIdent "Alice"`, and to take it apart you pattern-match: `match tok with | TIdent name -> ...`.

Closest analogues: Rust enums, Haskell ADTs, TypeScript discriminated unions. There's no equivalent in C# without a lot of boilerplate.

## The `PositionedToken` record

```fsharp
type PositionedToken = { Token: Token; Line: int }
```

A **record** — F#'s named-tuple / lightweight class. Immutable by default. You build one with `{ Token = TAttacks; Line = 5 }` and access fields with `pt.Token`. Field names are part of the type, so the compiler can infer the record type from the field names alone.

## Helper predicates

```fsharp
let private isIdentStart c = System.Char.IsLetter c || c = '_'
let private isIdentCont c = System.Char.IsLetterOrDigit c || c = '_'
```

`let` binds a name. `let f x = ...` is a function — F# doesn't need `function` keyword or `return`. The last expression is the result. `private` keeps it inside the module.

Note function-call syntax: `System.Char.IsLetter c` — no parens, just space-separated arguments. Parens are for grouping, not for calling.

## Keyword lookup

```fsharp
let private keywordOrIdent (s: string) =
    match s with
    | "attacks" -> TAttacks
    | "defends" -> TDefends
    ...
    | _ -> TIdent s
```

`match ... with` is pattern matching — like a `switch` that's an expression (returns a value) and is exhaustive (compiler warns if you miss cases). `_` is the wildcard "anything else." The `(s: string)` is a type annotation — usually you can omit them since type inference is strong, but for `string` we annotate because `match` on `_` alone could leave the type ambiguous.

## The `tokenize` function

```fsharp
let tokenize (source: string) : PositionedToken list =
```

Signature: takes a `string`, returns a `PositionedToken list`. The `: PositionedToken list` after the param is the return type annotation. F# uses `T list` syntax (postfix) for the built-in immutable linked list, equivalent to `List<T>` in other languages.

```fsharp
    let tokens = System.Collections.Generic.List<PositionedToken>()
    let mutable i = 0
    let mutable line = 1
```

Here I cheat on F#'s "immutable by default" stance for performance and clarity:

- `System.Collections.Generic.List` is the .NET `List<T>` (a growable array — same as `List<T>` in C#, *not* F#'s `list`). I use it because appending to F#'s immutable `list` in a hot loop would be O(n²).
- `let mutable i = 0` lets me reassign `i` with `i <- i + 1`. The `<-` arrow is mutation; plain `=` is binding/equality. Without `mutable`, `i` would be a constant.

This is idiomatic F# for lexers/parsers — pure-functional everywhere it doesn't cost you, mutable locals where iteration is genuinely the right model.

```fsharp
    let emit tok = tokens.Add { Token = tok; Line = line }
```

A **closure** — a local function that captures `tokens` and `line` from the enclosing scope. F# lets you define functions anywhere a `let` is valid, including inside another function. `{ Token = tok; Line = line }` constructs the record inline.

## The main loop

```fsharp
    while i < len do
        let c = source.[i]
        if c = '\n' then
            emit TNewline
            line <- line + 1
            i <- i + 1
        elif c = '\r' then
            ...
```

A few things at once:

- `while ... do ...` is the loop. The body is whatever follows, delimited by indentation (no `{ }` or `end`).
- `source.[i]` indexes a string. The dot before the bracket is mandatory in classic F#; `source[i]` works in newer F# but `.[i]` is the traditional form.
- `=` is **equality** in expression context (`c = '\n'`), and **binding** in `let` context (`let x = 5`). F# overloads it; you tell which by where it appears. There's no `==`.
- `elif` is `else if`. Indentation continues the same `if` chain.
- Inside each branch I do statements separated by newlines — no `;` needed.

The CRLF branch is interesting:

```fsharp
        elif c = '\r' then
            emit TNewline
            line <- line + 1
            i <- i + 1
            if i < len && source.[i] = '\n' then i <- i + 1
```

On `\r`, emit a newline, advance, and if the *next* char is `\n` (Windows line endings), skip it too so CRLF counts as one line break, not two.

## The identifier branch

```fsharp
        elif isIdentStart c then
            let start = i
            i <- i + 1
            while i < len && isIdentCont source.[i] do
                i <- i + 1
            emit (keywordOrIdent (source.Substring(start, i - start)))
```

Standard "munch as many identifier chars as you can" loop. Save the starting position, walk forward while the current char is letter/digit/underscore, then slice out the substring and look it up in the keyword table.

The parens around `(keywordOrIdent (source.Substring(start, i - start)))` are needed because function application is left-associative and tight: without them, `emit keywordOrIdent foo` would mean "call `emit` with three arguments: `keywordOrIdent`, `start`, `i - start`."

## The fallback

```fsharp
        else
            emit (TIdent (string c))
            i <- i + 1
```

If the char doesn't match anything, emit it as a one-character identifier. The parser will then fail with a clean "expected an action, got X" message on that line. This is a lazy choice — a stricter design would have a `TUnknown` token — but it keeps the lexer total and pushes error reporting into one place.

`string c` is calling the `string` conversion function (it converts a `char` to a `string`). F# has these handy converters at the top level: `int`, `string`, `float`, etc.

## Finishing up

```fsharp
    emit TEof
    List.ofSeq tokens
```

Always emit `TEof` so the parser has a sentinel it can safely peek at without bounds checks. Then convert the mutable `System.Collections.Generic.List` into an immutable F# `list` for the caller. `List.ofSeq` works on anything iterable (`seq<T>` = `IEnumerable<T>`).

## The `sourceLines` helper

```fsharp
let sourceLines (source: string) : string[] =
    source.Split([| '\n' |])
    |> Array.map (fun l -> l.TrimEnd('\r'))
```

Two pieces of new syntax:

- `[| '\n' |]` is an **array literal**. F# distinguishes `[ ... ]` (immutable list), `[| ... |]` (mutable array), and `seq { ... }` (lazy sequence).
- `|>` is the **pipe operator** — `x |> f` means `f x`. Reads left-to-right: take the split result, then map over it. Equivalent to `Array.map (...) (source.Split(...))` but much easier to read in chains.
- `(fun l -> l.TrimEnd('\r'))` is a lambda. `fun` introduces it, `->` separates params from body.

The result is a per-line array indexed by `line - 1`, so the parser can quote the offending line in error messages.

## Big takeaways for F#

1. **Whitespace is significant** — indentation defines blocks. Get used to keeping things lined up.
2. **`let` is everything** — values, functions, local helpers, all `let`.
3. **Immutable by default, mutable on request** with `mutable` + `<-`. The language nudges you toward immutability but doesn't fight you when you need a mutable counter.
4. **Pattern matching is the workhorse** — DUs + `match` replace most of the polymorphism and visitor patterns you'd use in C#/Java.
5. **The pipe operator (`|>`)** is everywhere in idiomatic F#. Get comfortable with it early; it makes data-transformation code very readable.
