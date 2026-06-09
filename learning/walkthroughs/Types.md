# Types.fs walkthrough

A short tour of `RPGCombatDSL/Types.fs`. This is the smallest file in the project — pure data definitions, no logic — which makes it ideal for nailing down F#'s record and discriminated-union syntax. Read [Lexer.md](./Lexer.md) first for the basics.

## The whole file

```fsharp
module Types

type Stat = { HP: int; Attack: int; Defense: int }

type Character = {
    Name: string
    Stats: Stat
}

type Action =
    | Attack of string
    | Defend
    | UseItem of string
    | CastSpell of string * string

type Turn = {
    Actor: string
    Action: Action
}
```

## Module declaration

```fsharp
module Types
```

Same as in the lexer and parser walkthroughs — top-of-file declaration puts everything below into the `Types` module. Other files reach in via `open Types` or by fully qualifying (`Types.Stat`).

## Records, two ways to format

`Stat` is written on a single line:

```fsharp
type Stat = { HP: int; Attack: int; Defense: int }
```

`Character` is written multi-line:

```fsharp
type Character = {
    Name: string
    Stats: Stat
}
```

These are identical in meaning — F# accepts either layout. The convention is single-line for small records and multi-line for anything with more than two or three fields or with longer field names. On a single line, semicolons separate fields. On multiple lines, newlines do the job and semicolons are optional.

## Records are nominal and structural

Records in F# are **nominal** — `Stat` and `Character.Stats` have a real, named type, not just "a thing with these fields." But they're also **structurally inferable**: when you write `{ HP = 100; Attack = 20; Defense = 10 }` somewhere in the code, the compiler figures out it must be a `Stat` because that combination of field names exists on exactly one record type. If two records share the same field set, you'd need a type annotation to disambiguate.

This is why throughout the project we never write `Stat { ... }` or `new Stat(...)` — just `{ HP = ...; ... }` is enough.

## Declaration order matters

`Stat` appears before `Character` because `Character` references it (`Stats: Stat`). F# files are processed top-to-bottom and the compiler must have seen a name before it's used.

This extends across files too — that's why `RPGCombatDSL.fsproj` lists `Types.fs` first, then `Engine.fs`, then `Lexer.fs`, then `Parser.fs`. Order in the project file is the compilation order, and it's deliberate, not alphabetical.

## The `Action` discriminated union

```fsharp
type Action =
    | Attack of string
    | Defend
    | UseItem of string
    | CastSpell of string * string
```

A DU where the cases carry different amounts of data:

- `Defend` carries nothing — it's just a tag.
- `Attack of string` carries a single string (the target's name).
- `UseItem of string` likewise (the item name).
- `CastSpell of string * string` carries **two** strings — spell name and target name.

That `string * string` is **tuple type syntax**. The `*` is read as "and" here, not multiplication: "a tuple of string and string." More generally, `int * string * bool` would be a three-tuple. Constructing one looks like:

```fsharp
CastSpell("Fireball", "Alice")
```

And destructuring in a pattern match:

```fsharp
| CastSpell(spell, target) -> ...
```

The parens around the tuple in the DU case are required at the call/match site to make the tuple-as-argument unambiguous. Compare with `Attack` which takes one string and doesn't need parens: `Attack "Bob"`.

## Naming collision: `Attack` the case vs. `Attack` the field

Notice that `Stat` has a field called `Attack`, and `Action` has a case called `Attack`. F# is fine with this because they live in different namespaces: one is `Stat.Attack` (a record field), the other is `Action.Attack` (a DU case). You'll see both in the codebase — `actor.Stats.Attack` (field access) vs. `Attack target` (constructing a DU case).

This kind of overloading is actually quite common in F# domain models and the compiler resolves it from context. If you ever hit an ambiguity, fully qualify: `Action.Attack`.

## The `Turn` record closes the loop

```fsharp
type Turn = {
    Actor: string
    Action: Action
}
```

A `Turn` pairs an actor's name with what they did. The whole DSL boils down to: parse a script into a `Turn list`, fold those turns over a `Map<string, Character>`, print the result. That entire data model fits in eighteen lines because DUs + records are dense.

## Concepts to take away

1. **Records are nominal but field-inferred.** You build them with `{ field = value; ... }` and the compiler resolves which type that is from the field names.
2. **DU cases can carry anything: nothing, one value, or a tuple.** `Defend`, `Attack of string`, `CastSpell of string * string`.
3. **`*` in a type means tuple, not multiplication.** Read `string * int * bool` as "string and int and bool."
4. **Declaration order matters within a file and across files** — F# is strictly top-down, and `.fsproj` order is the cross-file compilation order.
5. **Same name in different categories is fine.** A DU case `Attack` and a record field `Attack` don't conflict because they're disambiguated by context.
