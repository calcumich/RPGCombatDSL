# Narrative Trace Output — Design Spec

**Date:** 2026-06-10  
**Branch:** codex/report-parser-errors  
**Status:** Approved, ready for implementation

---

## Goal

Replace the current "print final HP" output with a turn-by-turn structured event log and a narrative renderer that produces human-readable battle prose. The event log is the primary artifact; the narrative renderer is one consumer of it, enabling future JSON export, replay, and frontend visualization without engine changes.

---

## Architecture

Four concerns, each in its own module:

| Module | Responsibility |
|---|---|
| `Types.fs` | Add `BattleEvent` DU; add `Trace` field to `BattleResult` |
| `Engine.fs` | Thread `BattleEvent list` through all action/statement functions |
| `Narrator.fs` | `render : BattleEvent list -> string` (new file) |
| `Program.fs` | Call `Narrator.render result.Trace` and print output |

---

## Section 1: `BattleEvent` type (in `Types.fs`)

```fsharp
type BattleEvent =
    | RoundStarted  of round: int
    | TurnTaken     of actor: string * action: Action
    | DamageDealt   of attacker: string * target: string * amount: int
    | HealApplied   of source: string * target: string * amount: int
    | StatBoosted   of actor: string * field: StatField * amount: int
    | TargetMissed  of actor: string * reason: string
    | CharDefeated  of name: string
    | BattleEnded   of outcome: BattleOutcome * rounds: int
```

`TurnTaken` records intent; effect events (`DamageDealt`, `HealApplied`, etc.) record outcome. This split lets a narrative renderer produce both "Alice attacks Bob" and "...for 14 damage." `TargetMissed` covers cases where `resolveTarget` returns `None`.

`BattleResult` gains one new field:

```fsharp
type BattleResult = {
    Outcome: BattleOutcome
    FinalState: Map<string, Character>
    RoundsCompleted: int
    Trace: BattleEvent list   // NEW
}
```

---

## Section 2: Engine changes (in `Engine.fs`)

### Signature changes

`applyActionWithTargeting` returns a tuple instead of bare state:

```fsharp
// Before
... (turn: Turn) : Map<string, Character>

// After
... (turn: Turn) : Map<string, Character> * BattleEvent list
```

`applyStatement` and `applyBattleAction` follow the same pattern.

### Event emission per action

| Action | Events emitted |
|---|---|
| `Attack` (hit) | `TurnTaken`, `DamageDealt`, optionally `CharDefeated` |
| `Attack` (no target) | `TurnTaken`, `TargetMissed` |
| `Defend` | `TurnTaken`, `StatBoosted(actor, Defense, 5)` |
| `UseItem "HealthPotion"` | `TurnTaken`, `HealApplied(actor, actor, 20)` |
| `UseItem "PowerPotion"` | `TurnTaken`, `StatBoosted(actor, Attack, 5)` |
| `UseItem "DefensePotion"` | `TurnTaken`, `StatBoosted(actor, Defense, 5)` |
| `CastSpell "Heal"` (hit) | `TurnTaken`, `HealApplied(actor, target, 15)` |
| `CastSpell "Fireball"` (hit) | `TurnTaken`, `DamageDealt`, optionally `CharDefeated` |
| Any spell/item (no target) | `TurnTaken`, `TargetMissed` |

### Threading through `applyStatement`

`SRepeat` uses `List.fold`; the accumulator becomes `(state, events)`:

```fsharp
[1..count]
|> List.fold
    (fun (state, evts) _ ->
        let (s2, newEvts) = List.fold applyStatementFold (state, []) body
        (s2, evts @ newEvts))
    (characters, [])
```

`SIf` recursively calls `applyStatement` on the chosen branch and propagates its events.

### `runBattle` accumulation

- Emits `RoundStarted round` at the top of each round.
- Folds over characters, accumulating `(state, events)` per round.
- Appends `BattleEnded` when the loop exits.
- Attaches the full accumulated list to `BattleResult.Trace`.

### Public `applyAction` (test surface)

The public `applyAction` keeps its current signature by discarding events:

```fsharp
let applyAction characters turn =
    applyActionWithTargeting true characters turn |> fst
```

Existing tests require no changes.

---

## Section 3: Narrative renderer (new `Narrator.fs`)

A new module with one public function:

```fsharp
module Narrator

open Types

let render (trace: BattleEvent list) : string =
    trace
    |> List.map (function
        | RoundStarted round        -> sprintf "--- Round %d ---" round
        | TurnTaken _               -> ""   // effect events carry the story
        | DamageDealt(a, t, n)      -> sprintf "%s strikes %s for %d damage." a t n
        | HealApplied(src, t, n)    -> sprintf "%s restores %d HP to %s." src n t
        | StatBoosted(a, f, n)      -> sprintf "%s gains +%d %A." a n f
        | TargetMissed(a, reason)   -> sprintf "%s finds no target (%s)." a reason
        | CharDefeated name         -> sprintf "%s has been defeated!" name
        | BattleEnded(Winner s, r)  -> sprintf "%s wins after %d rounds." s r
        | BattleEnded(Draw, r)      -> sprintf "Draw after %d rounds." r)
    |> List.filter (fun s -> s <> "")
    |> String.concat "\n"
```

`TurnTaken` is suppressed in narrative output (mapped to `""` and filtered). It remains in the event log for structured consumers.

`Program.fs` replaces the current HP-summary loop with `printfn "%s" (Narrator.render result.Trace)`.

---

## Section 4: Testing

### Engine tests (`EngineTests.fs`)

- Existing tests pass unchanged (public `applyAction` still returns `Map`).
- A new public function `applyActionWithEvents : Map<string, Character> -> Turn -> Map<string, Character> * BattleEvent list` is added alongside `applyAction` as the test-facing surface for event assertions.
- New tests call `applyActionWithEvents` and assert on both the state and the event list.
- Example: attack that kills a target must emit `[TurnTaken _; DamageDealt _; CharDefeated _]`.

### Narrator tests (new `NarratorTests.fs`)

- Pass hand-crafted `BattleEvent list` values to `Narrator.render`.
- Assert on the output string.
- Completely isolated from the engine.

### Integration tests

- Existing `runBattle` tests remain valid.
- New tests assert on `result.Trace`: e.g. a battle that ends with a winner must contain exactly one `BattleEnded(Winner _, _)` event; `RoundStarted` count must equal `result.RoundsCompleted`.

---

## Deliverables

1. `Types.fs` — `BattleEvent` DU + `Trace` field on `BattleResult`
2. `Engine.fs` — tuple-returning action/statement functions, event accumulation in `runBattle`
3. `Narrator.fs` — new module, `render` function
4. `Program.fs` — use `Narrator.render` for output
5. `EngineTests.fs` — new event-assertion tests
6. `NarratorTests.fs` — new file, narrative output tests
7. `learning/walkthroughs/Narrator.md` — walkthrough covering the `BattleEvent` DU, the renderer pattern, and example output

---

## Out of scope

- JSON export (enabled by the event log but not implemented here)
- Replay / step-through mode
- Frontend visualization
- Property-based testing (FsCheck)
