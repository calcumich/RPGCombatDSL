# Narrator.fs walkthrough

This file explains how battle events are structured and how the narrative renderer turns them into human-readable output.

## The `BattleEvent` discriminated union

`BattleEvent` is defined in `Types.fs`. It captures everything meaningful that happens during a battle as plain F# data:

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

`TurnTaken` records **intent** (what the actor tried to do). The events that follow it (`DamageDealt`, `HealApplied`, etc.) record the **effect**. This split means a renderer can say both "Alice attacks Bob" and "...for 14 damage."

## How events flow through the engine

`applyActionWithTargeting` in `Engine.fs` is the core function. It returns a tuple `(Map<string, Character> * BattleEvent list)` — the new state alongside every event that happened during the action.

For example, a successful attack emits:

```fsharp
newState, [TurnTaken(turn.Actor, turn.Action); DamageDealt(turn.Actor, targetName, damage)]
// plus CharDefeated if the target's HP dropped to 0 or below
```

`runBattle` accumulates events across every turn of every round, prepending `RoundStarted` at the top of each round and appending `BattleEnded` when the loop exits. The full list is stored in `BattleResult.Trace`.

## The renderer

`Narrator.render` in `Narrator.fs` is a pure function: `BattleEvent list -> string`. It pattern-matches each event into a prose line, filters out the empty strings (from suppressed `TurnTaken` events), and joins with newlines:

```fsharp
| DamageDealt(a, t, n)     -> sprintf "%s strikes %s for %d damage." a t n
| HealApplied(src, t, n)   -> sprintf "%s restores %d HP to %s." src n t
| StatBoosted(a, f, n)     -> sprintf "%s gains +%d %A." a n f
| CharDefeated name        -> sprintf "%s has been defeated!" name
| BattleEnded(Winner s, r) -> sprintf "%s wins after %d rounds." s r
```

`TurnTaken` maps to `""` and is filtered out — the effect events tell the story.

## Example output

Given Alice (20 atk / 5 def) vs Bob (10 atk / 2 def), where Alice goes first:

```
--- Round 1 ---
Alice strikes Bob for 18 damage.
Bob strikes Alice for 5 damage.
--- Round 2 ---
Alice strikes Bob for 18 damage.
Bob has been defeated!
heroes wins after 2 rounds.
```

## Why plain data instead of side effects

Because `BattleEvent list` is a value, you can:
- Assert on it in tests without mocking
- Feed it to a JSON serializer for structured export
- Replay it by folding over a fresh initial state
- Pass it to a different renderer (HTML, terminal color, speech) without touching the engine
