# Collections and Option walkthrough

A tour of the collection and option patterns introduced in `RPGCombatDSL/Engine.fs`. Read [Engine.md](./Engine.md) first — this builds on the pattern-matching and record-update foundations covered there.

The motivating example is `resolveTarget`, a private helper that turns a `TargetSpec` (either a literal name or a selector like "weakest enemy") into a concrete character name — or `None` if no valid candidate exists.

## The Option type

Before the collection pipeline, there's a return type that appears constantly in idiomatic F#:

```fsharp
let private resolveTarget
        (characters: Map<string, Character>)
        (actorName: string)
        (spec: TargetSpec) : string option =
```

`string option` means "either a string or nothing." It's a built-in discriminated union with exactly two cases:

```fsharp
// Conceptually (it's in the standard library, not your code):
type 'a option =
    | Some of 'a
    | None
```

The `'a` is a **type parameter** — `string option`, `int option`, `Character option` all use the same `option` type with different payloads. Think of it as F#'s null-safe replacement for nullable references. If a function might not produce a result, it returns `option` instead of returning `null` or throwing an exception.

You construct an option with `Some` or `None`:

```fsharp
Some "Bob"      // string option with a value
None            // string option with no value
```

You consume one with pattern matching:

```fsharp
match resolveTarget characters actorName spec with
| None -> characters                  // no target found — return world unchanged
| Some targetName ->                  // found one — use it
    let target = characters.[targetName]
    ...
```

The compiler forces you to handle both cases. There's no way to accidentally skip the "no result" branch — unlike nullable references, where forgetting a null check causes a runtime crash.

## The `NamedTarget` branch

```fsharp
match spec with
| NamedTarget name ->
    if characters.ContainsKey name then Some name else None
```

Straightforward: if the literal name exists in the map, wrap it in `Some` and return it. Otherwise return `None`. The `if/then/else` here is an expression — both branches return `string option`, so the whole thing reduces to one value.

This replaces the old `when characters.ContainsKey targetName` guard. The guard approach was implicit — a missing target silently fell through to `| _ -> characters`. The `option` approach is explicit: `resolveTarget` says out loud that it might return nothing, and callers are forced to handle that.

## The `TargetSelector` branch: building a pipeline

```fsharp
| TargetSelector(modifier, group) ->
    let actorSide = characters.[actorName].Side
    let candidates =
        characters
        |> Map.toSeq
        |> Seq.map snd
        |> Seq.filter (fun ch ->
            match group with
            | EnemyGroup      -> ch.Side <> actorSide
            | AllyGroup       -> ch.Side = actorSide
            | AnyGroup        -> true
            | NamedGroup name -> ch.Side = name)
        |> Seq.toList
    ...
```

This is a **collection pipeline** — a series of transformations chained with `|>`. Each step takes the output of the previous step and produces a new sequence. Reading top-to-bottom:

### `Map.toSeq`

```fsharp
characters
|> Map.toSeq
```

`Map<string, Character>` isn't directly iterable as a sequence of values — it's a key-value structure. `Map.toSeq` converts it into a `seq<string * Character>`: a lazy sequence of `(name, character)` pairs.

A `seq<_>` in F# is lazy — elements are produced on demand, not all at once. The pipeline stays lazy until something materializes it (like `Seq.toList` at the end). This means the filter runs without building an intermediate list first.

### `Seq.map snd`

```fsharp
|> Seq.map snd
```

`Seq.map f` applies `f` to every element and returns a new sequence of the results — the same concept as `Array.map` or LINQ's `.Select()`.

`snd` is a built-in function: `snd (a, b) = b`. It extracts the second element of a tuple. Since each element coming out of `Map.toSeq` is a `(string * Character)` pair, `snd` discards the key and keeps the `Character`. After this step we have `seq<Character>`.

The equivalent long form would be `Seq.map (fun (_, character) -> character)` — `snd` is just the shortest way to say "I only want the second element."

### `Seq.filter` with a nested match

```fsharp
|> Seq.filter (fun ch ->
    match group with
    | EnemyGroup      -> ch.Side <> actorSide
    | AllyGroup       -> ch.Side = actorSide
    | AnyGroup        -> true
    | NamedGroup name -> ch.Side = name)
```

`Seq.filter predicate` keeps only the elements for which `predicate` returns `true`. The predicate here is a lambda (`fun ch -> ...`) that pattern-matches on `group` — a `Group` DU value captured from the outer pattern match.

A few things happening at once:

- **Closures**: the lambda closes over `group` and `actorSide` from the enclosing scope. F# lambdas naturally capture surrounding bindings.
- **`match` as an expression**: the `match group with ...` produces a `bool` result in each arm (`<>`, `=`, `true`, or `=`). That bool is what `Seq.filter` uses. There's no explicit `return` — the value of the last expression in an arm is the result.
- **Exhaustive**: all four `Group` cases are covered, so the compiler won't warn about missing patterns. Adding a new case to `Group` would immediately break this match with a warning, forcing every call site to handle it — one of the key benefits of DUs over stringly-typed alternatives.

### `Seq.toList`

```fsharp
|> Seq.toList
```

This materializes the lazy sequence into an `'a list` — F#'s immutable, singly-linked list. After this step, all the filtering has run and we have a concrete `Character list` in `candidates`.

`Seq.toList` is the natural end of a sequence pipeline when you need to:
- Check `.IsEmpty` (sequences don't support this efficiently)
- Index into the result (`candidates.[idx]`)
- Use `List.*` functions like `List.minBy`

## Selecting from candidates

```fsharp
if candidates.IsEmpty then None
else
    match modifier with
    | Weakest   -> candidates |> List.minBy (fun ch -> ch.Stats.HP) |> fun ch -> Some ch.Name
    | Strongest -> candidates |> List.maxBy (fun ch -> ch.Stats.HP) |> fun ch -> Some ch.Name
    | Random    ->
        let idx = System.Random.Shared.Next(candidates.Length)
        Some candidates.[idx].Name
```

If the filtered list is empty, there's no valid target — return `None`. Otherwise, choose based on the `Modifier`.

### `List.minBy` and `List.maxBy`

```fsharp
candidates |> List.minBy (fun ch -> ch.Stats.HP)
```

`List.minBy projection` scans the list and returns the element for which `projection` produces the smallest value. The projection is a function from element to comparable — here, `fun ch -> ch.Stats.HP` extracts the HP integer. The result is the `Character` with the lowest HP.

`List.maxBy` is the mirror: highest projection value wins.

Both functions return the element itself (a `Character`), not the projected value. On a tie, they return the first qualifying element in iteration order — for `Map.toSeq` that's alphabetical by key, which is deterministic and testable.

### Piping into a one-off lambda

```fsharp
|> fun ch -> Some ch.Name
```

After `List.minBy` returns a `Character`, we pipe it into an inline lambda that extracts the name and wraps it in `Some`. This is idiomatic F# for "transform the result of a pipeline in one more step without naming an intermediate binding."

The equivalent without the pipe:

```fsharp
let winner = List.minBy (fun ch -> ch.Stats.HP) candidates
Some winner.Name
```

Both are fine. The piped form keeps the reading direction consistent — everything flows left-to-right.

### `.NET interop: `System.Random.Shared`

```fsharp
let idx = System.Random.Shared.Next(candidates.Length)
Some candidates.[idx].Name
```

`System.Random.Shared` is a .NET 6+ static property that returns a shared `Random` instance — no need to construct one. `.Next(n)` returns a random integer in `[0, n)`. `candidates.[idx]` is list indexing (O(n) for linked lists, fine for small candidate pools). This is regular .NET API access from F# — no wrapper needed.

## How callers use the result

In `applyAction`, both the `Attack` and `CastSpell` arms call `resolveTarget` and immediately match on the result:

```fsharp
| Attack spec ->
    match resolveTarget characters turn.Actor spec with
    | None -> characters
    | Some targetName ->
        let target = characters.[targetName]
        let damage = max 1 (actor.Stats.Attack - target.Stats.Defense)
        ...
```

This is the canonical option-consuming pattern. The `None` arm returns the unmodified world state — a no-op. The `Some` arm binds the resolved name to `targetName` and proceeds with the existing attack logic.

Notice the compiler would reject this code if either arm were missing. That's the contract `option` enforces: you wrote a function that might not find a target, and the compiler makes sure every call site handles that possibility.

## Concepts to take away

1. **`'a option` is a two-case DU: `Some value` or `None`.** It's F#'s null-safe way to represent "might not have a result." The compiler forces callers to handle both cases.
2. **`Map.toSeq` converts a map to a lazy sequence of `(key, value)` pairs.** Use it to start a collection pipeline over a map's contents.
3. **`Seq.map f` transforms each element; `Seq.filter pred` keeps only matching elements.** Both are lazy — no computation happens until something materializes the sequence.
4. **`snd` extracts the second element of a tuple.** It's shorthand for `fun (_, b) -> b`. `fst` does the first element.
5. **`Seq.toList` materializes a lazy sequence into an immutable list.** Do this when you need `.IsEmpty`, indexing, or `List.*` functions.
6. **`List.minBy` and `List.maxBy` take a projection function and return the element with the min/max projected value** — not the value itself. Ties go to the first element in iteration order.
7. **`|> fun x -> ...` is a concise way to transform a pipeline result in-place** without naming an intermediate binding.
8. **F# has full access to .NET APIs.** `System.Random.Shared.Next()` works without any wrapper.
