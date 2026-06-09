# Engine.fs walkthrough

A tour of `RPGCombatDSL/Engine.fs`. Read [Types.md](./Types.md) and [Lexer.md](./Lexer.md) first — this file is where the data model from `Types.fs` finally does something, and it leans heavily on pattern matching and a record-update syntax that surprises everyone the first time they see it.

## The whole function, at a glance

There's only one public function: `applyAction`. It takes the current world state (a map of characters by name) and a single turn, and returns the new world state. Everything else is pattern-matching on the turn's action and updating stats.

## Setup

```fsharp
module Engine
open Types
```

Module declaration, then `open Types` to bring `Character`, `Stat`, `Turn`, and `Action` into scope so we can write `Character` instead of `Types.Character`.

## The function signature

```fsharp
let applyAction (characters: Map<string, Character>) (turn: Turn) : Map<string, Character> =
```

A few things at once:

- **`Map<string, Character>`** is F#'s **immutable** map (not the .NET `Dictionary<K, V>`, which is mutable). Adding or removing entries returns a new map; the old one is unchanged.
- Two parameters in **curried form** — each in its own parens. This is the default in F# and the reason it works so naturally with `|>`. You could partially apply: `applyAction someMap` is itself a function `Turn -> Map<string, Character>`.
- The return type annotation `: Map<string, Character>` after the parameters but before the `=` says "this function returns a new map."

This signature is the whole API. Everything below is implementation.

## Looking up the actor

```fsharp
    let actor = characters.[turn.Actor]
```

`turn.Actor` is the actor's name (a `string`). `characters.[name]` is map lookup — same `.[ ]` indexing syntax used for arrays and strings.

**Caveat (real bug):** this throws `KeyNotFoundException` if the actor isn't in the map. A safer version would use `Map.tryFind`, which returns `Option<Character>` (`Some c` or `None`). The current parser doesn't validate that actors exist, so a script like `Ghost attacks Alice` would crash here. Worth flagging — a candidate for the next round of polish.

## The big pattern match

```fsharp
    match turn.Action with
    | Attack targetName when characters.ContainsKey targetName ->
        ...
    | Defend ->
        ...
    | UseItem itemName ->
        ...
    | CastSpell(spellName, targetName) when characters.ContainsKey targetName ->
        ...
    | _ ->
        characters
```

Pattern matching on the `Action` DU. A few patterns to absorb:

### Destructuring a DU case with one payload

```fsharp
| Attack targetName -> ...
```

`Attack` carries one `string`. The pattern `Attack targetName` matches the case **and** binds the payload to `targetName` for use in the branch body. No parens needed.

### Destructuring a DU case with a tuple payload

```fsharp
| CastSpell(spellName, targetName) -> ...
```

`CastSpell of string * string` carries a tuple. The pattern `CastSpell(spellName, targetName)` matches the case and unpacks the tuple in one step. The parens here are required because they're tuple syntax — they aren't function-call parens.

### Guard clauses with `when`

```fsharp
| Attack targetName when characters.ContainsKey targetName -> ...
```

The `when <expression>` after a pattern is a **guard clause** — the pattern only matches if the boolean expression is also true. So this arm fires only when the action is `Attack` *and* the target exists in the map. If the target doesn't exist, this arm is skipped and the match continues looking at later arms (eventually hitting `| _ -> characters`, which leaves the world unchanged).

### The wildcard at the end

```fsharp
| _ -> characters
```

`_` matches anything not matched above — in this case, `Attack` or `CastSpell` against a missing target. It returns `characters` unchanged, so invalid actions become silent no-ops. That's a design choice worth questioning eventually (you might prefer an error), but for a demo engine it keeps things simple.

## Record updates with `with`

This is the syntax that catches everyone off guard the first time. Here's the simplest example from the file:

```fsharp
let updatedActor = { actor with Stats = { actor.Stats with Defense = actor.Stats.Defense + 5 } }
```

Reading the inside-out:

1. `{ actor.Stats with Defense = actor.Stats.Defense + 5 }` — "give me a copy of `actor.Stats`, but with `Defense` replaced by the new value." The result is a new `Stat` record.
2. `{ actor with Stats = <that new Stat> }` — "give me a copy of `actor`, but with `Stats` replaced by the new `Stat`." The result is a new `Character`.

The general pattern is `{ original with Field1 = newValue1; Field2 = newValue2 }`. Records are immutable, so "update" really means "build a fresh record with most fields copied and these few replaced." The `with` keyword is sugar for that.

**The nesting is painful.** When you need to update a field two layers deep — like bumping HP, which lives at `character.Stats.HP` — you write the nested `{ ... with Stats = { Stats with HP = ... } }` dance every time. It's verbose. The mature solution is **optics / lenses** (the `FSharp.Lens` or `Aether` libraries), which let you write something like `character ^.-> Stat.HP_ <- newValue`. Overkill for this project, but worth knowing exists when the nesting starts to hurt.

## The `Attack` branch in detail

```fsharp
    | Attack targetName when characters.ContainsKey targetName ->
        let target = characters.[targetName]
        let damage = max 1 (actor.Stats.Attack - target.Stats.Defense)
        let updatedTarget = { target with Stats = { target.Stats with HP = target.Stats.HP - damage } }
        characters |> Map.add targetName updatedTarget
```

- `let target = characters.[targetName]` — look up the target. Safe here because the guard already confirmed they exist.
- `max 1 (actor.Stats.Attack - target.Stats.Defense)` — `max` is a stdlib function. It takes two arguments and returns the larger. The `max 1 ...` floor ensures every attack does at least 1 damage even against a high-defense target. Parens around the subtraction are required because of how function application binds.
- `characters |> Map.add targetName updatedTarget` — pipe the map into `Map.add`, which returns a new map with `targetName` mapped to `updatedTarget`. Equivalent to `Map.add targetName updatedTarget characters`. The pipe reads more naturally: "take the characters map, then add this update."

`Map.add` doesn't mutate `characters`; it returns a fresh map. The old one still exists and is unchanged. This is what "immutable map" really means in practice.

## The `UseItem` branch — match inside a match

```fsharp
    | UseItem itemName ->
        let updatedActor =
            match itemName.ToLowerInvariant() with
            | "healthpotion" -> { actor with Stats = { actor.Stats with HP = actor.Stats.HP + 20 } }
            | "powerpotion" -> { actor with Stats = { actor.Stats with Attack = actor.Stats.Attack + 5 } }
            | "defensepotion" -> { actor with Stats = { actor.Stats with Defense = actor.Stats.Defense + 5 } }
            | _ -> actor
        characters |> Map.add turn.Actor updatedActor
```

Two things worth flagging:

- A nested `match` — the outer match decides what kind of action this is; the inner match decides which item was used. `match` expressions return values, so the result of the inner match is bound to `updatedActor`.
- `itemName.ToLowerInvariant()` is a regular .NET string method call. F# uses .NET strings, so the whole `System.String` API is available. The case-insensitive lookup means scripts can write `HealthPotion`, `healthpotion`, or `HEALTHPOTION`.
- Unknown items fall through to `| _ -> actor`, leaving the actor unchanged. Same silent-failure choice as the outer wildcard.

## The `CastSpell` branch

```fsharp
    | CastSpell(spellName, targetName) when characters.ContainsKey targetName ->
        let target = characters.[targetName]
        let updatedTarget =
            match spellName.ToLowerInvariant() with
            | "heal" -> { target with Stats = { target.Stats with HP = target.Stats.HP + 15 } }
            | "fireball" ->
                let damage = max 1 (30 - target.Stats.Defense)
                { target with Stats = { target.Stats with HP = target.Stats.HP - damage } }
            | _ -> target
        characters |> Map.add targetName updatedTarget
```

Same shape as `UseItem`, but the inner match dispatches on spell name and the modifications apply to the *target*, not the actor. The `"fireball"` arm needs two statements (compute damage, then build the updated record), so it uses a multi-line branch.

Note that "heal" cast on a target heals them — there's no friend/foe distinction. The parser also allows `Bob casts Fireball on Bob`, which is a perfectly cromulent way to lose. The DSL is intentionally rope-to-hang-yourself-with.

## Concepts to take away

1. **`Map<K, V>` is immutable.** `Map.add` returns a new map. The old one doesn't change.
2. **DU pattern matching destructures payloads.** `Attack name` binds the string; `CastSpell(spell, target)` unpacks the tuple. Parens distinguish tuple destructuring from function calls.
3. **Guard clauses** (`when <bool>`) let a pattern match conditionally — useful for "the case shape matches but I also need this side condition true."
4. **The record-update `with` syntax** builds a new record from an old one with some fields replaced. Nested updates need nested `with` — verbose but explicit.
5. **`match` is an expression that returns a value.** That's why you can write `let x = match ... with | ... -> ...` and assign its result to a binding.
6. **`|>` shines with curried stdlib functions** like `Map.add k v`. The pipe lets you build pipelines that read top-to-bottom and left-to-right instead of inside-out.
