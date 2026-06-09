# Parser.fs walkthrough

A section-by-section tour of `RPGCombatDSL/Parser.fs`. Read [Lexer.md](./Lexer.md) first — this file builds on the same concepts (discriminated unions, records, pattern matching, `mutable`, pipes) without re-explaining them.

## Module and opens

```fsharp
module Parser

open Types
open Lexer
```

`open` is F#'s version of `using` (C#) / `import` (Python). It brings the contents of a module into scope so you can write `Turn` instead of `Types.Turn`, and `tokenize` instead of `Lexer.tokenize`.

## The public result types

```fsharp
type ParseError = {
    LineNumber: int
    LineText: string
    Message: string
}

type ParseResult = {
    Turns: Turn list
    Errors: ParseError list
}
```

Two records. Worth noticing: F#'s style for multi-field records uses the layout above — opening brace on its own line, one field per line, no commas required (newlines work as separators inside `{ }` blocks). This is purely cosmetic; `{ LineNumber: int; LineText: string; Message: string }` on one line is equivalent.

`Turn list` and `ParseError list` are the postfix list syntax we saw in `Lexer.fs` — immutable singly-linked lists.

## The mutable cursor

```fsharp
type private Cursor = {
    mutable Pos: int
    Tokens: PositionedToken[]
    Lines: string[]
}
```

Two new things:

- `type private Cursor` — the type itself is private to this module. Anything in here is an internal implementation detail; callers only see `parseScript` / `parseTurns`.
- `mutable Pos: int` — a **mutable field** in a record. Unusual in F# but exactly what we want: a single record we can pass around whose `Pos` advances as we consume tokens. The other two fields (`Tokens`, `Lines`) are immutable arrays we set once at construction.

You could model this purely functionally by threading `Pos` through every function as an extra argument and returning the new position with every result — but it adds a lot of plumbing for no real benefit when the cursor is local to one module. Pragmatic F# accepts this.

`PositionedToken[]` is the array type — `T[]` is shorthand for `array<T>`, the .NET mutable array. We use an array (not a list) because the parser does random access by index.

## Tiny cursor helpers

```fsharp
let private peek (c: Cursor) = c.Tokens.[c.Pos]

let private advance (c: Cursor) =
    if c.Pos < c.Tokens.Length - 1 then c.Pos <- c.Pos + 1
```

`peek` returns the current token without consuming it. `advance` moves forward, but never past the last position — that's `TEof`, our sentinel, and we want repeated peeks at end-of-input to keep returning `TEof` instead of crashing with an index-out-of-range.

Note `c.Pos <- c.Pos + 1` is mutation of a record field. The `<-` arrow only works because we declared `Pos` as `mutable` on the record.

## Error construction

```fsharp
let private lineText (c: Cursor) lineNumber =
    let idx = lineNumber - 1
    if idx >= 0 && idx < c.Lines.Length then c.Lines.[idx].Trim() else ""

let private err (c: Cursor) line message =
    { LineNumber = line; LineText = lineText c line; Message = message }
```

`lineText` is an `if ... then ... else` expression — meaning it *returns a value*. F#'s `if` always has both branches when used as an expression, and both branches must produce the same type. There's no statement-form `if` like C; everything is an expression.

`err` builds a `ParseError` record. The compiler infers it's a `ParseError` (and not some other record) by looking at the field names — `{ LineNumber; LineText; Message }` is unique to `ParseError`. No constructor call needed.

## Error recovery

```fsharp
let private recoverToNextLine (c: Cursor) =
    while (peek c).Token <> TNewline && (peek c).Token <> TEof do
        advance c
    if (peek c).Token = TNewline then advance c
```

When a line fails to parse, we skip forward until we hit a newline or EOF, then consume the newline so the next iteration starts fresh on the next line. This is what lets one bad line not kill the rest of the script.

`<>` is "not equal" — F#'s version of `!=`. There is no `!=` operator.

`(peek c).Token` — parens force grouping. Without them, `peek c.Token` would parse as `peek (c.Token)` because dot access binds tighter than function application. Read it as: "call `peek` on `c`, then take `.Token` from the result."

## The `Result` type

Before the next function, a key idea. F# has a built-in DU in the standard library:

```fsharp
type Result<'T, 'E> =
    | Ok of 'T
    | Error of 'E
```

It's an either-type for things that can succeed (`Ok value`) or fail (`Error reason`). Apostrophe-prefixed names like `'T` and `'E` are **generic type parameters** — F#'s notation for what other languages spell `<T, E>`.

Throughout the parser, our internal functions return `Result<Action, ParseError>` or `Result<Turn, ParseError>`. The caller checks which case it got with pattern matching.

## `expectIdent`

```fsharp
let private expectIdent (c: Cursor) (whatFor: string) : Result<string, ParseError> =
    let t = peek c
    match t.Token with
    | TIdent name ->
        advance c
        Ok name
    | _ ->
        Error (err c t.Line (sprintf "Expected %s" whatFor))
```

"Consume an identifier or fail." `whatFor` is a short phrase like `"target name after 'attacks'"` used to build the error message.

`sprintf "Expected %s" whatFor` — this is F#'s typed string formatter. `%s` requires a `string`, `%d` requires an `int`, etc. The compiler checks the arguments at compile time, which is a small superpower compared to C#'s `string.Format`.

Both branches of the `match` return a `Result<string, ParseError>`. The first arm advances the cursor (a side effect) *and* returns `Ok name`. F# is happy with this — there's no enforced purity, side effects and pure expressions mix freely.

## `parseAction` — the heart of the grammar

```fsharp
let private parseAction (c: Cursor) : Result<Action, ParseError> =
    let t = peek c
    match t.Token with
    | TAttacks ->
        advance c
        expectIdent c "target name after 'attacks'" |> Result.map Attack
    | TDefends ->
        advance c
        Ok Defend
    | TUses ->
        advance c
        expectIdent c "item name after 'uses'" |> Result.map UseItem
    | TCasts ->
        advance c
        match expectIdent c "spell name after 'casts'" with
        | Error e -> Error e
        | Ok spell ->
            match (peek c).Token with
            | TOn ->
                advance c
                expectIdent c "target name after 'on'"
                |> Result.map (fun target -> CastSpell(spell, target))
            | _ ->
                Ok (CastSpell(spell, ""))
    | _ ->
        Error (err c t.Line "Expected an action (attacks, defends, uses, casts)")
```

Lots happening — let's break it down.

### `Result.map Attack`

```fsharp
expectIdent c "target name after 'attacks'" |> Result.map Attack
```

`Result.map f r` says: if `r` is `Ok x`, return `Ok (f x)`; if `r` is `Error e`, leave it alone and return `Error e`. So this whole line means "parse an identifier; if successful, wrap it in `Attack`; otherwise keep the error."

The surprising bit: `Attack` is being used **as a function**. Because `Attack` is a DU case carrying a `string` (recall `| Attack of string` from `Types.fs`), the compiler treats it as a function `string -> Action`. So `Result.map Attack` is "apply the `Attack` constructor to whatever's inside the `Ok`." Same trick with `UseItem`.

This is one of the most idiomatic patterns in F# — DU constructors are values you can pass to higher-order functions.

### Nested `match` for `casts`

The `TCasts` branch needs to do two steps in sequence and propagate any error from the first. The "long-hand" version:

```fsharp
match expectIdent c "spell name after 'casts'" with
| Error e -> Error e        // propagate the error
| Ok spell ->                // got the spell, now look for 'on <target>'
    match (peek c).Token with
    | TOn -> ...
    | _ -> Ok (CastSpell(spell, ""))
```

Real-world F# would often use a `computation expression` (`result { let! spell = ...; ... }`) or a custom bind operator to flatten this — but written out, the control flow is obvious, which is good for a learning project.

### `(fun target -> CastSpell(spell, target))`

```fsharp
|> Result.map (fun target -> CastSpell(spell, target))
```

A lambda. We can't write `Result.map CastSpell` here because `CastSpell` takes two arguments and we only have one to give (the `target` from `expectIdent`); we need to pair it with the `spell` captured from the outer `Ok spell` branch. The lambda closes over `spell`.

## `parseTurn`

```fsharp
let private parseTurn (c: Cursor) : Result<Turn, ParseError> =
    let startTok = peek c
    match startTok.Token with
    | TIdent actor ->
        advance c
        parseAction c
        |> Result.map (fun action -> { Actor = actor; Action = action })
    | _ ->
        Error (err c startTok.Line "Expected an actor name at start of line")
```

Same shape. Parse the actor name, then delegate to `parseAction`, then if that succeeded, build a `Turn` record from the parts.

`{ Actor = actor; Action = action }` constructs a `Turn` — again the field names tell the compiler which record type.

## `parseScript` — putting it together

```fsharp
let parseScript (script: string) : ParseResult =
    let tokens = tokenize script |> List.toArray
    let cursor = { Pos = 0; Tokens = tokens; Lines = sourceLines script }

    let turns = System.Collections.Generic.List<Turn>()
    let errors = System.Collections.Generic.List<ParseError>()
```

Setup: lex the source into tokens, wrap them in a cursor, and create two mutable .NET lists for accumulating results. Same trick as the lexer — F#'s immutable `list` would be O(n²) for appending; the .NET `List<T>` is O(1) amortized.

```fsharp
    let rec loop () =
        match (peek cursor).Token with
        | TEof -> ()
        | TNewline ->
            advance cursor
            loop ()
        | _ ->
            match parseTurn cursor with
            | Ok turn ->
                turns.Add turn
                match (peek cursor).Token with
                | TEof -> ()
                | TNewline -> advance cursor
                | _ ->
                    let t = peek cursor
                    errors.Add (err cursor t.Line "Unexpected tokens after turn")
                    recoverToNextLine cursor
            | Error e ->
                errors.Add e
                recoverToNextLine cursor
            loop ()

    loop ()
    { Turns = List.ofSeq turns; Errors = List.ofSeq errors }
```

A few language points:

- `let rec loop () = ...` — `let rec` allows the function to call itself. Plain `let` doesn't; the compiler treats the name as not-yet-in-scope inside its own body unless you use `rec`. (For mutually recursive functions, you'd use `let rec ... and ...`.)
- `()` is the **unit value** — F#'s equivalent of `void`. Functions that don't return anything meaningful return `unit`, written `()`. `loop ()` calls `loop` with the unit argument; `| TEof -> ()` returns unit, ending the loop.
- The recursive calls `loop ()` are in **tail position** — they're the last thing each branch does. F# guarantees tail-call optimization in tail position, so this won't blow the stack even on very long scripts.

The logic itself: peek, decide.

- `TEof` → done, fall out of the loop.
- `TNewline` → blank line, just consume it and continue.
- Anything else → try to parse a turn. If it works, the next token must be a newline or EOF (otherwise there's leftover garbage on the line). If it fails, record the error and skip the rest of the line. Either way, loop again.

Final line constructs the `ParseResult` record from the accumulated lists.

## `parseTurns`

```fsharp
let parseTurns (script: string) : Result<Turn list, ParseError list> =
    let result = parseScript script
    match result.Errors with
    | [] -> Ok result.Turns
    | errors -> Error errors
```

Convenience wrapper for callers who want all-or-nothing semantics. `parseScript` always returns both lists; `parseTurns` collapses to `Ok` only when there are no errors.

`| [] ->` is **pattern matching on a list** — `[]` is the empty-list pattern. The other arm binds the whole list to `errors`. F# pattern matching on lists also supports `head :: tail` shape: `| first :: rest -> ...` would bind the first element and the rest.

## Concepts to take away

1. **`Result<T, E>`** is the bread-and-butter error type in F#. `Ok` / `Error` for the cases, `Result.map` to transform the success case without touching the error case.
2. **DU constructors are functions.** `Attack`, `UseItem`, `Ok`, `Error` — anywhere a function is expected, you can pass a DU case that takes one argument and it acts like a constructor function.
3. **Mutable when local, immutable at the boundaries.** Inside `parseScript` we use mutation freely; the public surface (`ParseResult`, `Result<Turn list, _>`) is all immutable.
4. **`let rec` + tail calls** are how F# does loops that are awkward to express as `while`. Tail-position recursion compiles to a real loop, so there's no stack-depth concern.
5. **Pattern matching on lists** with `[]` and `head :: tail` shows up constantly — get fluent with it.
