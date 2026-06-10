# Types.fs walkthrough

A short tour of `RPGCombatDSL/Types.fs`. This is the smallest file in the project — pure data definitions, no logic — which makes it ideal for nailing down F#'s record and discriminated-union syntax. Read [Lexer.md](./Lexer.md) first for the basics.

## The whole file

```fsharp
module Types

type Stat = { HP: int; Attack: int; Defense: int }

type Character = {
    Name: string
    Stats: Stat
    Side: string
}

type Modifier =
    | Weakest
    | Strongest
    | Random

type Group =
    | EnemyGroup
    | AllyGroup
    | AnyGroup
    | NamedGroup of string

type TargetSpec =
    | NamedTarget of string
    | TargetSelector of Modifier * Group

type Action =
    | Attack of TargetSpec
    | Defend
    | UseItem of string
    | CastSpell of string * TargetSpec

type Turn = {
    Actor: string
    Action: Action
}

[<RequireQualifiedAccess>]
type StatField =
    | HP
    | Attack
    | Defense

type Expr =
    | EIntLit of int
    | EStatRef of character: string * field: StatField

type Comparator =
    | Lt | Le | Gt | Ge | Eq | Ne

type Condition =
    | Compare of Expr * Comparator * Expr

type Statement =
    | SAction   of Turn
    | SIf       of Condition * thenBranch: Statement * elseBranch: Statement option
    | STeamDecl of teamName: string * members: string list
    | SRepeat   of count: int * body: Statement list
```

## Module declaration

```fsharp
module Types
```

Same as in the other walkthroughs — top-of-file declaration puts everything below into the `Types` module. Other files reach in via `open Types` or by fully qualifying (`Types.Stat`).

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
    Side: string
}
```

These are identical in meaning — F# accepts either layout. The convention is single-line for small records and multi-line for anything with more than two or three fields or with longer field names. On a single line, semicolons separate fields. On multiple lines, newlines do the job and semicolons are optional.

`Side` is a plain `string` — `"heroes"`, `"villains"`, or `""` for no affiliation. The battle setup assigns sides; the DSL itself doesn't declare them.

## Records are nominal and structural

Records in F# are **nominal** — `Stat` and `Character.Stats` have a real, named type, not just "a thing with these fields." But they're also **structurally inferable**: when you write `{ HP = 100; Attack = 20; Defense = 10 }` somewhere in the code, the compiler figures out it must be a `Stat` because that combination of field names exists on exactly one record type. If two records share the same field set, you'd need a type annotation to disambiguate.

This is why throughout the project we never write `Stat { ... }` or `new Stat(...)` — just `{ HP = ...; ... }` is enough.

## Declaration order matters

`Stat` appears before `Character` because `Character` references it (`Stats: Stat`). F# files are processed top-to-bottom and the compiler must have seen a name before it's used.

This extends across files too — that's why `RPGCombatDSL.fsproj` lists `Types.fs` first, then `Engine.fs`, then `Lexer.fs`, then `Parser.fs`. Order in the project file is the compilation order, and it's deliberate, not alphabetical.

## Three helper DUs: `Modifier`, `Group`, and `TargetSpec`

Before `Action`, there are three small discriminated unions that exist purely to describe how a target is specified:

```fsharp
type Modifier =
    | Weakest
    | Strongest
    | Random

type Group =
    | EnemyGroup
    | AllyGroup
    | AnyGroup
    | NamedGroup of string

type TargetSpec =
    | NamedTarget of string
    | TargetSelector of Modifier * Group
```

`Modifier` is a **tag-only** DU — its cases carry no data, making it closer to an enum than to a typical DU. You construct one just by writing the case name: `Weakest`, `Strongest`, `Random`.

`Group` is a mix: three tag-only cases (`EnemyGroup`, `AllyGroup`, `AnyGroup`) for relative targeting, and one data-carrying case (`NamedGroup of string`) for targeting by team name. `NamedGroup "Heroes"` is as valid a `Group` value as `EnemyGroup` — they just carry different amounts of information.

`TargetSpec` is the interesting one. It has two cases with completely different shapes:

- `NamedTarget of string` — a literal character name, like `NamedTarget "Bob"`.
- `TargetSelector of Modifier * Group` — a pair of the two DUs above: which *method* to use (`Weakest`, `Strongest`, `Random`) and which *pool of candidates* to draw from (`EnemyGroup`, `AllyGroup`, `AnyGroup`).

## DU within DU: `TargetSelector of Modifier * Group`

The payload of `TargetSelector` is a **tuple of two DU values**, not two strings. This is worth dwelling on.

Compare these two DU cases from elsewhere in the file:

```fsharp
CastSpell of string * string   // old Action case
```

vs.

```fsharp
TargetSelector of Modifier * Group
```

Both carry a two-element tuple. But in the first, both slots are raw strings — the compiler can't stop you from writing `CastSpell("Alice", "Fireball")` when you meant `CastSpell("Fireball", "Alice")`. In the second, the slots have distinct types: you can't accidentally pass a `Group` where a `Modifier` is expected. The compiler enforces the contract.

**Constructing** a `TargetSelector` looks like nested DU cases:

```fsharp
TargetSelector(Weakest, EnemyGroup)
TargetSelector(Random, AllyGroup)
```

**Destructuring** in a pattern match unpacks them in one step:

```fsharp
match spec with
| NamedTarget name -> ...               // name is a string
| TargetSelector(modifier, group) -> ... // modifier: Modifier, group: Group
```

From there you can nest another match:

```fsharp
| TargetSelector(modifier, group) ->
    match modifier with
    | Weakest   -> ...
    | Strongest -> ...
    | Random    -> ...
```

This nesting is natural in F# — each `match` is itself an expression that produces a value, so you can chain as many as you need.

## `Action` gets a richer payload type

```fsharp
type Action =
    | Attack of TargetSpec
    | Defend
    | UseItem of string
    | CastSpell of string * TargetSpec
```

`Attack` used to carry a bare `string` (the target's name). It now carries a `TargetSpec`, which can be either a literal name or a selector. The `CastSpell` case still carries the spell name as a `string`, but the target is now a `TargetSpec` too.

This is the principle sometimes called **make illegal states unrepresentable**. The old `Attack of string` let any string through — an actor could attack `"nonsense"`, `""`, or any garbage value; the engine would silently do nothing. The new `Attack of TargetSpec` only admits values that were explicitly constructed: `NamedTarget "Bob"`, `TargetSelector(Weakest, EnemyGroup)`, etc. The type itself documents what's valid.

The cost is that pattern matching on `Attack` now requires unwrapping one more layer:

```fsharp
// Old
| Attack targetName -> ...

// New
| Attack (NamedTarget name) -> ...
| Attack (TargetSelector(modifier, group)) -> ...
// or, treating the whole spec as one thing:
| Attack spec -> match resolveTarget ... spec with ...
```

In practice the engine takes the last form — it hands the whole `TargetSpec` to a separate function that resolves it to a name, keeping `applyAction` clean.

## The `Turn` record closes the loop

```fsharp
type Turn = {
    Actor: string
    Action: Action
}
```

A `Turn` pairs an actor's name with what they did. The whole DSL boils down to: parse a script into a `Statement list`, fold those statements over a `Map<string, Character>`, print the result.

## The lower half of the file: DSL expression types

Everything below `Turn` supports the `if/then/else` conditional syntax. These types form a small expression tree:

```fsharp
type StatField = | HP | Attack | Defense

type Expr =
    | EIntLit of int
    | EStatRef of character: string * field: StatField
```

`StatField` uses the `[<RequireQualifiedAccess>]` attribute, which forces callers to write `StatField.HP` instead of just `HP`. This avoids ambiguity since `HP` could otherwise look like anything — it's explicit that this is a stat field, not an action or a case from some other DU.

`Expr` can be either a literal integer (`EIntLit 30`) or a reference to a character's stat (`EStatRef("Bob", StatField.HP)`). The second form uses **named tuple fields** in the DU declaration:

```fsharp
EStatRef of character: string * field: StatField
```

The names (`character:`, `field:`) are documentation only — they don't change how you construct or destructure the value. `EStatRef("Bob", StatField.HP)` is the constructor; `EStatRef(name, field)` is the pattern. The names just help readers understand what each slot means.

```fsharp
type Condition =
    | Compare of Expr * Comparator * Expr

type Statement =
    | SAction of Turn
    | SIf of Condition * thenBranch: Statement * elseBranch: Statement option
```

`Statement` is **recursive** in two ways. `SIf` holds `thenBranch` and `elseBranch` as individual `Statement` values, enabling nested `if` expressions. `SRepeat` holds a `body: Statement list` — an arbitrary sequence of statements to run N times. Both forms are handled by a mutually recursive family of parse functions; see [Parser.md](./Parser.md).

`STeamDecl` and `SRepeat` are straightforward:
- `STeamDecl of teamName: string * members: string list` — carries the team's name and a list of member character names. The engine folds over `members` and sets each character's `Side` field.
- `SRepeat of count: int * body: Statement list` — carries the repeat count and the list of body statements. The engine runs the body `count` times, threading state through each iteration.

`Statement option` in `SIf`'s `elseBranch` introduces `Option` — a built-in DU with exactly two cases: `Some value` or `None`. It's F#'s null-safe alternative to nullable references. A `Statement option` is either `Some stmt` (there is an else branch) or `None` (there isn't). See [Collections.md](./Collections.md) for a full treatment of `Option`.

## Concepts to take away

1. **Records are nominal but field-inferred.** You build them with `{ field = value; ... }` and the compiler resolves which type that is from the field names.
2. **Tag-only DU cases are effectively enums.** `Weakest`, `EnemyGroup`, etc. carry no data — they're pure labels. But DUs can mix tag-only and data-carrying cases in the same type: `Group` has both (`AnyGroup` vs `NamedGroup of string`).
3. **DU payloads can themselves be DUs.** `TargetSelector of Modifier * Group` is safer than `string * string` because the types enforce which slot is which.
4. **Make illegal states unrepresentable.** Replacing `Attack of string` with `Attack of TargetSpec` moves validation from runtime to compile time — invalid targets can't be constructed.
5. **`*` in a type means tuple, not multiplication.** Read `Modifier * Group` as "a Modifier and a Group."
6. **Named tuple fields are documentation.** `EStatRef of character: string * field: StatField` — the names exist for readers, not the compiler.
7. **Declaration order matters within a file and across files** — F# is strictly top-down, and `.fsproj` order is the cross-file compilation order.
