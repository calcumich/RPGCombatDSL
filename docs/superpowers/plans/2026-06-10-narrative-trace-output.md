# Narrative Trace Output Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `BattleEvent` discriminated union that flows through the engine as first-class data, producing a turn-by-turn narrative of every battle via a `Narrator.render` function.

**Architecture:** `applyActionWithTargeting` returns `(Map<string, Character> * BattleEvent list)` instead of bare state; all callers thread events upward. The public `applyAction` and `applyStatement` keep their existing signatures by discarding events — existing tests require no changes. A new `Narrator.fs` module holds the renderer. `Program.fs` prints the narrative instead of the current HP summary.

**Tech Stack:** F# 8 / .NET 8, xUnit 2.9, `dotnet test` for test runs.

---

## File Map

| File | Change |
|---|---|
| `RPGCombatDSL/Types.fs` | Add `BattleEvent` DU; add `Trace: BattleEvent list` to `BattleResult` |
| `RPGCombatDSL/Engine.fs` | Thread events through `applyActionWithTargeting`, private `applyStatementImpl`, and `runBattle`; add public `applyActionWithEvents`; keep `applyAction`/`applyStatement` signatures unchanged |
| `RPGCombatDSL/Narrator.fs` | New file — `render : BattleEvent list -> string` |
| `RPGCombatDSL/RPGCombatDSL.fsproj` | Register `Narrator.fs` after `Engine.fs` |
| `RPGCombatDSL/Program.fs` | Replace HP-summary loop with `Narrator.render result.Trace` |
| `RPGCombatDSL.Tests/EngineTests.fs` | Add event-assertion tests for `applyActionWithEvents` and `runBattle` trace |
| `RPGCombatDSL.Tests/NarratorTests.fs` | New file — xUnit tests for `Narrator.render` |
| `RPGCombatDSL.Tests/RPGCombatDSL.Tests.fsproj` | Register `NarratorTests.fs` after `EngineTests.fs` |
| `learning/walkthroughs/Narrator.md` | Walkthrough for `BattleEvent`, renderer pattern, example output |

---

### Task 1: Add `BattleEvent` and `Trace` to `Types.fs`

**Files:**
- Modify: `RPGCombatDSL/Types.fs`
- Modify: `RPGCombatDSL/Engine.fs` (compile fix only — add `Trace = []`)
- Modify: `RPGCombatDSL.Tests/EngineTests.fs` (one new test to confirm Trace field exists)

- [ ] **Step 1: Write the failing test**

Add at the end of `RPGCombatDSL.Tests/EngineTests.fs`:

```fsharp
// ── Trace field ───────────────────────────────────────────────────────────────

[<Fact>]
let ``BattleResult exposes a Trace field`` () =
    let result: BattleResult = {
        Outcome = Draw
        FinalState = Map.empty
        RoundsCompleted = 0
        Trace = [ RoundStarted 1 ]
    }
    Assert.Equal(1, result.Trace.Length)
```

- [ ] **Step 2: Run the test to confirm it fails**

```
dotnet test RPGCombatDSL.Tests
```

Expected: compile error — `BattleResult` has no field `Trace`; `RoundStarted` is not defined.

- [ ] **Step 3: Add `BattleEvent` DU and `Trace` field**

In `RPGCombatDSL/Types.fs`, add the `BattleEvent` type after `BattleOutcome` and before `BattleResult`, then add `Trace` to `BattleResult`:

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

type BattleResult = {
    Outcome: BattleOutcome
    FinalState: Map<string, Character>
    RoundsCompleted: int
    Trace: BattleEvent list
}
```

- [ ] **Step 4: Fix the compile error in `Engine.fs`**

`runBattle` constructs `BattleResult` in two places. Both need `Trace = []` added:

```fsharp
// In the `Some outcome` branch:
Ok { Outcome = outcome; FinalState = state; RoundsCompleted = round; Trace = [] }

// In the `None when round >= maxRounds` branch:
Ok { Outcome = Draw; FinalState = state; RoundsCompleted = round; Trace = [] }
```

- [ ] **Step 5: Run tests to confirm all pass**

```
dotnet test RPGCombatDSL.Tests
```

Expected: all tests pass (including the new `BattleResult exposes a Trace field` test).

- [ ] **Step 6: Commit**

```
git add RPGCombatDSL/Types.fs RPGCombatDSL/Engine.fs RPGCombatDSL.Tests/EngineTests.fs
git commit -m "Add BattleEvent DU and Trace field to BattleResult"
```

---

### Task 2: Thread events through `applyActionWithTargeting` and expose `applyActionWithEvents`

**Files:**
- Modify: `RPGCombatDSL/Engine.fs`
- Modify: `RPGCombatDSL.Tests/EngineTests.fs`

- [ ] **Step 1: Write the failing tests**

Add the following block at the end of `RPGCombatDSL.Tests/EngineTests.fs`:

```fsharp
// ── applyActionWithEvents ────────────────────────────────────────────────────

[<Fact>]
let ``applyActionWithEvents always emits TurnTaken as first event`` () =
    let characters = [ actor "Alice" 100 20 5 "" ] |> Map.ofList
    let turn = { Actor = "Alice"; Action = Defend }

    let (_, events) = applyActionWithEvents characters turn

    Assert.Equal<BattleEvent>(TurnTaken("Alice", Defend), List.head events)

[<Fact>]
let ``applyActionWithEvents attack emits DamageDealt`` () =
    // damage = max 1 (20 - 2) = 18
    let characters =
        [ actor "Alice" 100 20 5 "heroes"; actor "Bob" 50 10 2 "villains" ]
        |> Map.ofList
    let turn = { Actor = "Alice"; Action = Attack(NamedTarget "Bob") }

    let (_, events) = applyActionWithEvents characters turn

    Assert.Contains(DamageDealt("Alice", "Bob", 18), events)

[<Fact>]
let ``applyActionWithEvents attack that defeats target emits CharDefeated`` () =
    // damage = max 1 (20 - 2) = 18, Bob HP = 10 → -8 (defeated)
    let characters =
        [ actor "Alice" 100 20 5 "heroes"; actor "Bob" 10 10 2 "villains" ]
        |> Map.ofList
    let turn = { Actor = "Alice"; Action = Attack(NamedTarget "Bob") }

    let (_, events) = applyActionWithEvents characters turn

    Assert.Contains(CharDefeated "Bob", events)

[<Fact>]
let ``applyActionWithEvents attack with no valid target emits TargetMissed`` () =
    let characters = [ actor "Alice" 100 20 5 "heroes" ] |> Map.ofList
    let turn = { Actor = "Alice"; Action = Attack(NamedTarget "Unknown") }

    let (_, events) = applyActionWithEvents characters turn

    Assert.Contains(TargetMissed("Alice", "no eligible target"), events)

[<Fact>]
let ``applyActionWithEvents defend emits StatBoosted Defense 5`` () =
    let characters = [ actor "Alice" 100 20 5 "" ] |> Map.ofList
    let turn = { Actor = "Alice"; Action = Defend }

    let (_, events) = applyActionWithEvents characters turn

    Assert.Contains(StatBoosted("Alice", StatField.Defense, 5), events)

[<Fact>]
let ``applyActionWithEvents UseItem HealthPotion emits HealApplied`` () =
    let characters = [ actor "Alice" 60 20 5 "" ] |> Map.ofList
    let turn = { Actor = "Alice"; Action = UseItem "HealthPotion" }

    let (_, events) = applyActionWithEvents characters turn

    Assert.Contains(HealApplied("Alice", "Alice", 20), events)

[<Fact>]
let ``applyActionWithEvents UseItem PowerPotion emits StatBoosted Attack 5`` () =
    let characters = [ actor "Alice" 100 20 5 "" ] |> Map.ofList
    let turn = { Actor = "Alice"; Action = UseItem "PowerPotion" }

    let (_, events) = applyActionWithEvents characters turn

    Assert.Contains(StatBoosted("Alice", StatField.Attack, 5), events)

[<Fact>]
let ``applyActionWithEvents UseItem DefensePotion emits StatBoosted Defense 5`` () =
    let characters = [ actor "Alice" 100 20 5 "" ] |> Map.ofList
    let turn = { Actor = "Alice"; Action = UseItem "DefensePotion" }

    let (_, events) = applyActionWithEvents characters turn

    Assert.Contains(StatBoosted("Alice", StatField.Defense, 5), events)

[<Fact>]
let ``applyActionWithEvents CastSpell Heal emits HealApplied`` () =
    let characters =
        [ actor "Cleric" 70 8 8 "heroes"; actor "Alice" 50 20 5 "heroes" ]
        |> Map.ofList
    let turn = { Actor = "Cleric"; Action = CastSpell("Heal", NamedTarget "Alice") }

    let (_, events) = applyActionWithEvents characters turn

    Assert.Contains(HealApplied("Cleric", "Alice", 15), events)

[<Fact>]
let ``applyActionWithEvents CastSpell Fireball emits DamageDealt`` () =
    // damage = max 1 (30 - 5) = 25
    let characters =
        [ actor "Alice" 100 20 5 "heroes"; actor "Bob" 100 10 5 "villains" ]
        |> Map.ofList
    let turn = { Actor = "Alice"; Action = CastSpell("Fireball", NamedTarget "Bob") }

    let (_, events) = applyActionWithEvents characters turn

    Assert.Contains(DamageDealt("Alice", "Bob", 25), events)
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test RPGCombatDSL.Tests
```

Expected: compile error — `applyActionWithEvents` is not defined.

- [ ] **Step 3: Rewrite `applyActionWithTargeting` to return events, update callers**

Replace the `applyActionWithTargeting`, `applyAction`, and `applyBattleAction` functions in `RPGCombatDSL/Engine.fs` with the following (keep all other functions unchanged):

```fsharp
let private applyActionWithTargeting
        (includeDefeated: bool)
        (characters: Map<string, Character>)
        (turn: Turn) : Map<string, Character> * BattleEvent list =
    match Map.tryFind turn.Actor characters with
    | None -> characters, []
    | Some actor ->
    let turnEvent = TurnTaken(turn.Actor, turn.Action)
    match turn.Action with
    | Attack spec ->
        match resolveTarget includeDefeated characters turn.Actor spec with
        | None ->
            characters, [turnEvent; TargetMissed(turn.Actor, "no eligible target")]
        | Some targetName ->
            let target = characters.[targetName]
            let damage = max 1 (actor.Stats.Attack - target.Stats.Defense)
            let updatedTarget = { target with Stats = { target.Stats with HP = target.Stats.HP - damage } }
            let newState = characters |> Map.add targetName updatedTarget
            let defeated = if updatedTarget.Stats.HP <= 0 then [CharDefeated targetName] else []
            newState, [turnEvent; DamageDealt(turn.Actor, targetName, damage)] @ defeated
    | Defend ->
        let updatedActor = { actor with Stats = { actor.Stats with Defense = actor.Stats.Defense + 5 } }
        characters |> Map.add turn.Actor updatedActor,
        [turnEvent; StatBoosted(turn.Actor, StatField.Defense, 5)]
    | UseItem itemName ->
        match itemName.ToLowerInvariant() with
        | "healthpotion" ->
            let updated = { actor with Stats = { actor.Stats with HP = actor.Stats.HP + 20 } }
            characters |> Map.add turn.Actor updated,
            [turnEvent; HealApplied(turn.Actor, turn.Actor, 20)]
        | "powerpotion" ->
            let updated = { actor with Stats = { actor.Stats with Attack = actor.Stats.Attack + 5 } }
            characters |> Map.add turn.Actor updated,
            [turnEvent; StatBoosted(turn.Actor, StatField.Attack, 5)]
        | "defensepotion" ->
            let updated = { actor with Stats = { actor.Stats with Defense = actor.Stats.Defense + 5 } }
            characters |> Map.add turn.Actor updated,
            [turnEvent; StatBoosted(turn.Actor, StatField.Defense, 5)]
        | _ ->
            characters |> Map.add turn.Actor actor, [turnEvent]
    | CastSpell(spellName, spec) ->
        match resolveTarget includeDefeated characters turn.Actor spec with
        | None ->
            characters, [turnEvent; TargetMissed(turn.Actor, "no eligible target")]
        | Some targetName ->
            let target = characters.[targetName]
            match spellName.ToLowerInvariant() with
            | "heal" ->
                let updated = { target with Stats = { target.Stats with HP = target.Stats.HP + 15 } }
                characters |> Map.add targetName updated,
                [turnEvent; HealApplied(turn.Actor, targetName, 15)]
            | "fireball" ->
                let damage = max 1 (30 - target.Stats.Defense)
                let updated = { target with Stats = { target.Stats with HP = target.Stats.HP - damage } }
                let newState = characters |> Map.add targetName updated
                let defeated = if updated.Stats.HP <= 0 then [CharDefeated targetName] else []
                newState, [turnEvent; DamageDealt(turn.Actor, targetName, damage)] @ defeated
            | _ ->
                characters, [turnEvent]

let applyAction (characters: Map<string, Character>) (turn: Turn) : Map<string, Character> =
    applyActionWithTargeting true characters turn |> fst

let applyActionWithEvents (characters: Map<string, Character>) (turn: Turn) : Map<string, Character> * BattleEvent list =
    applyActionWithTargeting true characters turn

let private applyBattleAction (characters: Map<string, Character>) (turn: Turn) : Map<string, Character> * BattleEvent list =
    applyActionWithTargeting false characters turn
```

- [ ] **Step 4: Run tests to confirm new tests pass and no regressions**

```
dotnet test RPGCombatDSL.Tests
```

Expected: all tests pass (new event tests pass; existing state-only tests still pass because `applyAction` discards events).

- [ ] **Step 5: Commit**

```
git add RPGCombatDSL/Engine.fs RPGCombatDSL.Tests/EngineTests.fs
git commit -m "Thread events through applyActionWithTargeting, expose applyActionWithEvents"
```

---

### Task 3: Thread events through `applyStatement` (internal)

**Files:**
- Modify: `RPGCombatDSL/Engine.fs`

This task changes the internal implementation of `applyStatement` so that it accumulates events. The public `applyStatement` signature is unchanged — existing tests remain green automatically.

- [ ] **Step 1: Replace `applyStatement` with an event-aware private implementation and a public wrapper**

In `RPGCombatDSL/Engine.fs`, replace the `applyStatement` function with:

```fsharp
let rec private applyStatementImpl
        (characters: Map<string, Character>)
        (stmt: Statement) : Map<string, Character> * BattleEvent list =
    match stmt with
    | SAction turn ->
        applyActionWithTargeting true characters turn
    | SIf(cond, thenBranch, elseBranch) ->
        if evalCondition characters cond then
            applyStatementImpl characters thenBranch
        else
            match elseBranch with
            | Some s -> applyStatementImpl characters s
            | None   -> characters, []
    | STeamDecl(teamName, members) ->
        let newState =
            members
            |> List.fold (fun state name ->
                match Map.tryFind name state with
                | Some ch -> Map.add name { ch with Side = teamName } state
                | None    -> state) characters
        newState, []
    | SRepeat(count, body) ->
        [1..count]
        |> List.fold
            (fun (state, evts) _ ->
                let (s2, newEvts) =
                    List.fold
                        (fun (st, ev) s ->
                            let (st2, ev2) = applyStatementImpl st s
                            (st2, ev @ ev2))
                        (state, [])
                        body
                (s2, evts @ newEvts))
            (characters, [])

let applyStatement (characters: Map<string, Character>) (stmt: Statement) : Map<string, Character> =
    applyStatementImpl characters stmt |> fst
```

- [ ] **Step 2: Run tests to confirm no regressions**

```
dotnet test RPGCombatDSL.Tests
```

Expected: all existing tests still pass (public `applyStatement` signature unchanged).

- [ ] **Step 3: Commit**

```
git add RPGCombatDSL/Engine.fs
git commit -m "Thread events through applyStatement via private applyStatementImpl"
```

---

### Task 4: Accumulate events in `runBattle` and add trace tests

**Files:**
- Modify: `RPGCombatDSL/Engine.fs`
- Modify: `RPGCombatDSL.Tests/EngineTests.fs`

- [ ] **Step 1: Write the failing tests**

Add to the end of `RPGCombatDSL.Tests/EngineTests.fs`:

```fsharp
// ── Battle trace ──────────────────────────────────────────────────────────────

[<Fact>]
let ``runBattle trace contains exactly one BattleEnded event`` () =
    let alice = snd (actor "Alice" 100 20 5 "heroes")
    let bob   = snd (actor "Bob"    30 10 5 "villains")
    let scripts =
        [ "Alice", [ SAction { Actor = "Alice"; Action = Attack(NamedTarget "Bob") } ]
          "Bob",   [ SAction { Actor = "Bob";   Action = Attack(NamedTarget "Alice") } ] ]
        |> Map.ofList

    let result = runBattle defaultBattleConfig [ alice; bob ] scripts

    match result with
    | Ok battle ->
        let endEvents = battle.Trace |> List.filter (function BattleEnded _ -> true | _ -> false)
        Assert.Equal(1, List.length endEvents)
    | Error errors -> Assert.Fail(sprintf "%A" errors)

[<Fact>]
let ``runBattle trace RoundStarted count equals RoundsCompleted`` () =
    let alice = snd (actor "Alice" 100 20 5 "heroes")
    let bob   = snd (actor "Bob"    30 10 5 "villains")
    let scripts =
        [ "Alice", [ SAction { Actor = "Alice"; Action = Attack(NamedTarget "Bob") } ]
          "Bob",   [ SAction { Actor = "Bob";   Action = Attack(NamedTarget "Alice") } ] ]
        |> Map.ofList

    let result = runBattle defaultBattleConfig [ alice; bob ] scripts

    match result with
    | Ok battle ->
        let roundsStarted =
            battle.Trace
            |> List.filter (function RoundStarted _ -> true | _ -> false)
            |> List.length
        Assert.Equal(battle.RoundsCompleted, roundsStarted)
    | Error errors -> Assert.Fail(sprintf "%A" errors)

[<Fact>]
let ``runBattle trace BattleEnded carries correct winner`` () =
    let alice = snd (actor "Alice" 100 20 5 "heroes")
    let bob   = snd (actor "Bob"    30 10 5 "villains")
    let scripts =
        [ "Alice", [ SAction { Actor = "Alice"; Action = Attack(NamedTarget "Bob") } ]
          "Bob",   [ SAction { Actor = "Bob";   Action = Attack(NamedTarget "Alice") } ] ]
        |> Map.ofList

    let result = runBattle defaultBattleConfig [ alice; bob ] scripts

    match result with
    | Ok battle ->
        Assert.Contains(BattleEnded(Winner "heroes", battle.RoundsCompleted), battle.Trace)
    | Error errors -> Assert.Fail(sprintf "%A" errors)
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test RPGCombatDSL.Tests
```

Expected: new trace tests fail — `battle.Trace` is always `[]` at this point.

- [ ] **Step 3: Update `runBattle` to accumulate events**

In `RPGCombatDSL/Engine.fs`, replace the `runRound` inner function and its call inside `runBattle` with:

```fsharp
let rec runRound round state allEvents =
    match battleOutcome state with
    | Some outcome ->
        let trace = allEvents @ [BattleEnded(outcome, round)]
        Ok { Outcome = outcome; FinalState = state; RoundsCompleted = round; Trace = trace }
    | None when round >= maxRounds ->
        let trace = allEvents @ [BattleEnded(Draw, round)]
        Ok { Outcome = Draw; FinalState = state; RoundsCompleted = round; Trace = trace }
    | None ->
        let folder acc actor =
            match acc with
            | Error _ as error -> error
            | Ok (state, evts) ->
                match Map.tryFind actor.Name state with
                | Some currentActor when currentActor.Stats.HP > 0 ->
                    let statements = behaviorScripts.[actor.Name]
                    match chooseBehaviorAction actor.Name state statements with
                    | Error message ->
                        Error [{ Character = actor.Name; Message = message }]
                    | Ok None -> Ok (state, evts)
                    | Ok (Some turn) ->
                        let (newState, turnEvts) = applyBattleAction state turn
                        Ok (newState, evts @ turnEvts)
                | _ -> Ok (state, evts)

        let roundStartEvents = [RoundStarted(round + 1)]
        match List.fold folder (Ok (state, roundStartEvents)) initialOrder with
        | Error errors -> Error errors
        | Ok (nextState, roundEvts) ->
            runRound (round + 1) nextState (allEvents @ roundEvts)

runRound 0 initialState []
```

- [ ] **Step 4: Run tests to confirm all pass**

```
dotnet test RPGCombatDSL.Tests
```

Expected: all tests pass including the three new trace tests.

- [ ] **Step 5: Commit**

```
git add RPGCombatDSL/Engine.fs RPGCombatDSL.Tests/EngineTests.fs
git commit -m "Accumulate BattleEvent list in runBattle, populate BattleResult.Trace"
```

---

### Task 5: Create `Narrator.fs` and `NarratorTests.fs`

**Files:**
- Create: `RPGCombatDSL/Narrator.fs`
- Create: `RPGCombatDSL.Tests/NarratorTests.fs`
- Modify: `RPGCombatDSL/RPGCombatDSL.fsproj`
- Modify: `RPGCombatDSL.Tests/RPGCombatDSL.Tests.fsproj`

- [ ] **Step 1: Register `Narrator.fs` in the main project**

In `RPGCombatDSL/RPGCombatDSL.fsproj`, add `Narrator.fs` after `Engine.fs`:

```xml
<ItemGroup>
  <Compile Include="Types.fs" />
  <Compile Include="Engine.fs" />
  <Compile Include="Narrator.fs" />
  <Compile Include="Lexer.fs" />
  <Compile Include="Parser.fs" />
  <Compile Include="Program.fs" />
</ItemGroup>
```

- [ ] **Step 2: Register `NarratorTests.fs` in the test project**

In `RPGCombatDSL.Tests/RPGCombatDSL.Tests.fsproj`, add `NarratorTests.fs` after `EngineTests.fs`:

```xml
<ItemGroup>
  <Compile Include="LexerTests.fs" />
  <Compile Include="ParserTests.fs" />
  <Compile Include="EngineTests.fs" />
  <Compile Include="NarratorTests.fs" />
</ItemGroup>
```

- [ ] **Step 3: Create `NarratorTests.fs` with failing tests**

Create `RPGCombatDSL.Tests/NarratorTests.fs`:

```fsharp
module NarratorTests

open Types
open Narrator
open Xunit

[<Fact>]
let ``render RoundStarted`` () =
    Assert.Equal("--- Round 2 ---", render [ RoundStarted 2 ])

[<Fact>]
let ``render TurnTaken is suppressed`` () =
    Assert.Equal("", render [ TurnTaken("Alice", Defend) ])

[<Fact>]
let ``render DamageDealt`` () =
    Assert.Equal("Alice strikes Bob for 15 damage.", render [ DamageDealt("Alice", "Bob", 15) ])

[<Fact>]
let ``render HealApplied`` () =
    Assert.Equal("Cleric restores 15 HP to Alice.", render [ HealApplied("Cleric", "Alice", 15) ])

[<Fact>]
let ``render StatBoosted Defense`` () =
    Assert.Equal("Alice gains +5 Defense.", render [ StatBoosted("Alice", StatField.Defense, 5) ])

[<Fact>]
let ``render StatBoosted Attack`` () =
    Assert.Equal("Alice gains +5 Attack.", render [ StatBoosted("Alice", StatField.Attack, 5) ])

[<Fact>]
let ``render TargetMissed`` () =
    Assert.Equal(
        "Alice finds no target (no eligible target).",
        render [ TargetMissed("Alice", "no eligible target") ])

[<Fact>]
let ``render CharDefeated`` () =
    Assert.Equal("Bob has been defeated!", render [ CharDefeated "Bob" ])

[<Fact>]
let ``render BattleEnded Winner`` () =
    Assert.Equal("heroes wins after 3 rounds.", render [ BattleEnded(Winner "heroes", 3) ])

[<Fact>]
let ``render BattleEnded Draw`` () =
    Assert.Equal("Draw after 5 rounds.", render [ BattleEnded(Draw, 5) ])

[<Fact>]
let ``render multiple events joined by newline, TurnTaken suppressed`` () =
    let events =
        [ RoundStarted 1
          TurnTaken("Alice", Attack(NamedTarget "Bob"))
          DamageDealt("Alice", "Bob", 15)
          CharDefeated "Bob"
          BattleEnded(Winner "heroes", 1) ]
    let expected =
        "--- Round 1 ---\nAlice strikes Bob for 15 damage.\nBob has been defeated!\nheroes wins after 1 rounds."
    Assert.Equal(expected, render events)
```

- [ ] **Step 4: Run tests to confirm they fail**

```
dotnet test RPGCombatDSL.Tests
```

Expected: compile error — module `Narrator` is not defined.

- [ ] **Step 5: Create `Narrator.fs`**

Create `RPGCombatDSL/Narrator.fs`:

```fsharp
module Narrator

open Types

let render (trace: BattleEvent list) : string =
    trace
    |> List.map (function
        | RoundStarted round        -> sprintf "--- Round %d ---" round
        | TurnTaken _               -> ""
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

- [ ] **Step 6: Run tests to confirm all pass**

```
dotnet test RPGCombatDSL.Tests
```

Expected: all tests pass including the 11 new Narrator tests.

- [ ] **Step 7: Commit**

```
git add RPGCombatDSL/Narrator.fs RPGCombatDSL/RPGCombatDSL.fsproj RPGCombatDSL.Tests/NarratorTests.fs RPGCombatDSL.Tests/RPGCombatDSL.Tests.fsproj
git commit -m "Add Narrator module with render function and full test coverage"
```

---

### Task 6: Update `Program.fs` to use `Narrator.render`

**Files:**
- Modify: `RPGCombatDSL/Program.fs`

- [ ] **Step 1: Replace the HP-summary loop with `Narrator.render`**

In `RPGCombatDSL/Program.fs`, replace the `Ok result ->` branch inside `main`:

```fsharp
| Ok result ->
    printfn "%s" (Narrator.render result.Trace)
    0
```

The full `main` function becomes:

```fsharp
[<EntryPoint>]
let main _ =
    match parseBehaviorScripts behaviorScripts with
    | Ok scripts ->
        match runBattle defaultBattleConfig characters scripts with
        | Ok result ->
            printfn "%s" (Narrator.render result.Trace)
            0
        | Error errors ->
            for error in errors do
                printfn "Battle error for %s: %s" error.Character error.Message
            1
    | Error errors ->
        for name, error in errors do
            printfn "Parse error in %s on line %d: %s (%s)" name error.LineNumber error.Message error.LineText
        1
```

- [ ] **Step 2: Run the program to verify narrative output**

```
dotnet run --project RPGCombatDSL
```

Expected: turn-by-turn narrative output ending with a winner line. Example:

```
--- Round 1 ---
Alice strikes Bob for 15 damage.
Bob casts Fireball on Alice for 25 damage.
...
heroes wins after N rounds.
```

- [ ] **Step 3: Run tests to confirm no regressions**

```
dotnet test RPGCombatDSL.Tests
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```
git add RPGCombatDSL/Program.fs
git commit -m "Replace HP summary with Narrator.render narrative output in Program.fs"
```

---

### Task 7: Write `Narrator.md` walkthrough

**Files:**
- Create: `learning/walkthroughs/Narrator.md`

- [ ] **Step 1: Create the walkthrough**

Create `learning/walkthroughs/Narrator.md`:

```markdown
# Narrator.fs walkthrough

This file explains how battle events are structured and how the narrative renderer turns them into human-readable output.

## The `BattleEvent` discriminated union

`BattleEvent` is defined in `Types.fs`. It captures everything meaningful that happens during a battle as plain F# data:

\`\`\`fsharp
type BattleEvent =
    | RoundStarted  of round: int
    | TurnTaken     of actor: string * action: Action
    | DamageDealt   of attacker: string * target: string * amount: int
    | HealApplied   of source: string * target: string * amount: int
    | StatBoosted   of actor: string * field: StatField * amount: int
    | TargetMissed  of actor: string * reason: string
    | CharDefeated  of name: string
    | BattleEnded   of outcome: BattleOutcome * rounds: int
\`\`\`

`TurnTaken` records **intent** (what the actor tried to do). The events that follow it (`DamageDealt`, `HealApplied`, etc.) record the **effect**. This split means a renderer can say both "Alice attacks Bob" and "...for 14 damage."

## How events flow through the engine

`applyActionWithTargeting` in `Engine.fs` is the core function. It now returns a tuple `(Map<string, Character> * BattleEvent list)` — the new state alongside every event that happened during the action.

For example, a successful attack emits:

\`\`\`fsharp
newState, [TurnTaken(turn.Actor, turn.Action); DamageDealt(turn.Actor, targetName, damage)]
// plus CharDefeated if the target's HP dropped to 0 or below
\`\`\`

`runBattle` accumulates events across every turn of every round, prepending `RoundStarted` at the top of each round and appending `BattleEnded` when the loop exits. The full list is stored in `BattleResult.Trace`.

## The renderer

`Narrator.render` in `Narrator.fs` is a pure function: `BattleEvent list -> string`. It pattern-matches each event into a prose line, filters out the empty strings (from suppressed `TurnTaken` events), and joins with newlines:

\`\`\`fsharp
| DamageDealt(a, t, n)     -> sprintf "%s strikes %s for %d damage." a t n
| HealApplied(src, t, n)   -> sprintf "%s restores %d HP to %s." src n t
| StatBoosted(a, f, n)     -> sprintf "%s gains +%d %A." a n f
| CharDefeated name        -> sprintf "%s has been defeated!" name
| BattleEnded(Winner s, r) -> sprintf "%s wins after %d rounds." s r
\`\`\`

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
```

- [ ] **Step 2: Commit**

```
git add learning/walkthroughs/Narrator.md
git commit -m "Add Narrator.md walkthrough covering BattleEvent, engine threading, and render"
```
