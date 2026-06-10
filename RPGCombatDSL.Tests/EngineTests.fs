module EngineTests

open Engine
open Types
open Xunit

let private actor name hp attack defense side =
    name,
    {
        Name = name
        Stats = { HP = hp; Attack = attack; Defense = defense }
        Side = side
    }

[<Fact>]
let ``attack action damages target`` () =
    let characters =
        [ actor "Alice" 100 20 5 "heroes"; actor "Bob" 50 10 2 "villains" ]
        |> Map.ofList

    let updated =
        applyAction characters { Actor = "Alice"; Action = Attack (NamedTarget "Bob") }

    Assert.Equal(32, updated["Bob"].Stats.HP)

[<Fact>]
let ``defend increases defense`` () =
    let characters =
        [ actor "Alice" 100 20 5 "" ]
        |> Map.ofList

    let updated =
        applyAction characters { Actor = "Alice"; Action = Defend }

    Assert.Equal(10, updated["Alice"].Stats.Defense)

[<Fact>]
let ``invalid attack leaves state unchanged`` () =
    let characters =
        [ actor "Alice" 100 20 5 "heroes" ]
        |> Map.ofList

    let updated =
        applyAction characters { Actor = "Alice"; Action = Attack (NamedTarget "Unknown") }

    Assert.Equal<Map<string, Character>>(characters, updated)

[<Fact>]
let ``attack always deals at least one damage`` () =
    let characters =
        [ actor "Bob" 90 15 5 "heroes"; actor "Tank" 40 3 100 "villains" ]
        |> Map.ofList

    let updated =
        applyAction characters { Actor = "Bob"; Action = Attack (NamedTarget "Tank") }

    Assert.Equal(39, updated["Tank"].Stats.HP)

[<Fact>]
let ``if condition true applies then branch`` () =
    let characters = [ actor "Bob" 20 10 5 "" ] |> Map.ofList
    let stmt =
        SIf(
            Compare(EStatRef("Bob", StatField.HP), Lt, EIntLit 30),
            SAction { Actor = "Bob"; Action = UseItem "HealthPotion" },
            None)

    let updated = applyStatement characters stmt

    Assert.Equal(40, updated["Bob"].Stats.HP)

[<Fact>]
let ``if condition false with no else leaves state unchanged`` () =
    let characters = [ actor "Bob" 80 10 5 "" ] |> Map.ofList
    let stmt =
        SIf(
            Compare(EStatRef("Bob", StatField.HP), Lt, EIntLit 30),
            SAction { Actor = "Bob"; Action = UseItem "HealthPotion" },
            None)

    let updated = applyStatement characters stmt

    Assert.Equal<Map<string, Character>>(characters, updated)

[<Fact>]
let ``if condition false applies else branch`` () =
    let characters =
        [ actor "Alice" 80 20 5 "heroes"; actor "Bob" 50 10 5 "villains" ] |> Map.ofList
    let stmt =
        SIf(
            Compare(EStatRef("Alice", StatField.HP), Lt, EIntLit 30),
            SAction { Actor = "Alice"; Action = Defend },
            Some (SAction { Actor = "Alice"; Action = Attack (NamedTarget "Bob") }))

    let updated = applyStatement characters stmt

    // Alice.HP (80) is not < 30, so else branch runs: Alice attacks Bob.
    // Damage = max 1 (20 - 5) = 15, so Bob.HP = 50 - 15 = 35.
    Assert.Equal(35, updated["Bob"].Stats.HP)
    Assert.Equal(5, updated["Alice"].Stats.Defense)

[<Fact>]
let ``evalExpr resolves stat reference via condition`` () =
    let characters = [ actor "Bob" 42 10 5 "" ] |> Map.ofList
    let stmt =
        SIf(
            Compare(EStatRef("Bob", StatField.HP), Eq, EIntLit 42),
            SAction { Actor = "Bob"; Action = Defend },
            None)

    let updated = applyStatement characters stmt

    Assert.Equal(10, updated["Bob"].Stats.Defense)

[<Fact>]
let ``weakest enemy selector targets the lowest-HP enemy`` () =
    let characters =
        [ actor "Alice" 100 20 5 "heroes"
          actor "Bob"    30 10 2 "villains"
          actor "Orc"    60  8 3 "villains" ]
        |> Map.ofList

    let updated =
        applyAction characters { Actor = "Alice"; Action = Attack (TargetSelector(Weakest, EnemyGroup)) }

    // Bob (30 HP, defense 2) is weakest enemy; damage = max 1 (20 - 2) = 18 → 12 HP
    Assert.Equal(12, updated["Bob"].Stats.HP)
    Assert.Equal(60, updated["Orc"].Stats.HP)

[<Fact>]
let ``strongest enemy selector targets the highest-HP enemy`` () =
    let characters =
        [ actor "Alice" 100 20 5 "heroes"
          actor "Bob"    30 10 2 "villains"
          actor "Orc"    60  8 3 "villains" ]
        |> Map.ofList

    let updated =
        applyAction characters { Actor = "Alice"; Action = Attack (TargetSelector(Strongest, EnemyGroup)) }

    // Orc (60 HP, defense 3) is strongest enemy; damage = max 1 (20 - 3) = 17 → 43 HP
    Assert.Equal(43, updated["Orc"].Stats.HP)
    Assert.Equal(30, updated["Bob"].Stats.HP)

[<Fact>]
let ``ally selector heals the lowest-HP same-side character`` () =
    let characters =
        [ actor "Cleric"  40  8 8 "heroes"
          actor "Alice"  100 20 5 "heroes"
          actor "Bob"     90 15 5 "villains" ]
        |> Map.ofList

    let updated =
        applyAction characters
            { Actor = "Cleric"; Action = CastSpell("Heal", TargetSelector(Weakest, AllyGroup)) }

    // Weakest ally of Cleric (heroes) is Cleric itself at 40 HP → healed to 55
    Assert.Equal(55, updated["Cleric"].Stats.HP)
    // Bob (enemy) must not be healed
    Assert.Equal(90, updated["Bob"].Stats.HP)

[<Fact>]
let ``enemy filter never targets same-side character`` () =
    let characters =
        [ actor "Alice"  100 20 5 "heroes"
          actor "Cleric"  80  8 5 "heroes"
          actor "Bob"     50 15 5 "villains" ]
        |> Map.ofList

    let updated =
        applyAction characters
            { Actor = "Alice"; Action = Attack (TargetSelector(Weakest, EnemyGroup)) }

    // Only Bob is an enemy; damage = max 1 (20 - 5) = 15 → 35 HP
    Assert.Equal(35, updated["Bob"].Stats.HP)
    Assert.Equal(80, updated["Cleric"].Stats.HP)

[<Fact>]
let ``no valid candidates is a no-op`` () =
    let characters =
        [ actor "Alice" 100 20 5 "heroes"
          actor "Bob"    50 15 5 "heroes" ]
        |> Map.ofList

    let updated =
        applyAction characters
            { Actor = "Alice"; Action = Attack (TargetSelector(Weakest, EnemyGroup)) }

    Assert.Equal<Map<string, Character>>(characters, updated)

[<Fact>]
let ``random selector with single candidate is deterministic`` () =
    let characters =
        [ actor "Alice" 100 20 5 "heroes"
          actor "Bob"    50 15 5 "villains" ]
        |> Map.ofList

    let updated =
        applyAction characters
            { Actor = "Alice"; Action = Attack (TargetSelector(Random, EnemyGroup)) }

    // Only Bob qualifies; damage = max 1 (20 - 5) = 15 → 35 HP
    Assert.Equal(35, updated["Bob"].Stats.HP)

// ── Team declarations ─────────────────────────────────────────────────────────

[<Fact>]
let ``applyStatement STeamDecl sets Side on named members`` () =
    let state =
        [ actor "Alice" 100 20 5 "unknown"
          actor "Bob"   100 20 5 "unknown" ]
        |> Map.ofList

    let result = applyStatement state (STeamDecl("Heroes", ["Alice"]))

    Assert.Equal("Heroes",  result["Alice"].Side)
    Assert.Equal("unknown", result["Bob"].Side)   // unaffected

[<Fact>]
let ``applyStatement STeamDecl silently ignores unknown names`` () =
    let state = [ actor "Alice" 100 20 5 "x" ] |> Map.ofList
    let result = applyStatement state (STeamDecl("T", ["NoOne"]))
    Assert.Equal<Map<string, Character>>(state, result)

[<Fact>]
let ``applyStatement STeamDecl reassigns multiple members`` () =
    let state =
        [ actor "Alice"  100 20 5 "old"
          actor "Cleric" 100 10 5 "old"
          actor "Goblin" 100 15 3 "old" ]
        |> Map.ofList

    let result = applyStatement state (STeamDecl("Heroes", ["Alice"; "Cleric"]))

    Assert.Equal("Heroes", result["Alice"].Side)
    Assert.Equal("Heroes", result["Cleric"].Side)
    Assert.Equal("old",    result["Goblin"].Side)  // unaffected

// ── Repeat blocks ─────────────────────────────────────────────────────────────

[<Fact>]
let ``applyStatement SRepeat applies body N times cumulatively`` () =
    // Alice: 20 atk, Bob: 5 def → damage = 15 per turn × 3
    let state =
        [ actor "Alice" 100 20 5 "heroes"
          actor "Bob"   100 20 5 "villains" ]
        |> Map.ofList
    let body = [ SAction { Actor = "Alice"; Action = Attack(NamedTarget "Bob") } ]

    let result = applyStatement state (SRepeat(3, body))

    Assert.Equal(100 - 15 * 3, result["Bob"].Stats.HP)

[<Fact>]
let ``applyStatement SRepeat 0 is a no-op`` () =
    let state =
        [ actor "Alice" 100 20 5 "heroes"
          actor "Bob"   100 20 5 "villains" ]
        |> Map.ofList
    let body = [ SAction { Actor = "Alice"; Action = Attack(NamedTarget "Bob") } ]

    let result = applyStatement state (SRepeat(0, body))

    Assert.Equal<Map<string, Character>>(state, result)

[<Fact>]
let ``applyStatement SRepeat sees live state between iterations`` () =
    // Bob defends each round (+5 defense), so damage decreases each iteration.
    // Round 1: Bob defends (def 5→10), Alice hits max 1 (20-10)=10. HP 90.
    // Round 2: Bob defends (def 10→15), Alice hits max 1 (20-15)=5. HP 85.
    // Round 3: Bob defends (def 15→20), Alice hits max 1 (20-20)=1. HP 84.
    let state =
        [ actor "Alice" 100 20 5 "heroes"
          actor "Bob"   100 20 5 "villains" ]
        |> Map.ofList
    let body =
        [ SAction { Actor = "Bob";   Action = Defend }
          SAction { Actor = "Alice"; Action = Attack(NamedTarget "Bob") } ]

    let result = applyStatement state (SRepeat(3, body))

    Assert.Equal(84, result["Bob"].Stats.HP)

// ── Named group targeting ─────────────────────────────────────────────────────

[<Fact>]
let ``applyAction NamedGroup filters candidates to matching Side`` () =
    let state =
        [ actor "Alice"  100 20 5 "Heroes"
          actor "Bob"    100 20 5 "Heroes"
          actor "Goblin"  40 15 3 "Villains" ]
        |> Map.ofList
    let updated =
        applyAction state { Actor = "Goblin"; Action = Attack(TargetSelector(Weakest, NamedGroup "Heroes")) }
    // Goblin is unaffected; one Hero took damage = max 1 (15 - 5) = 10
    Assert.Equal(40, updated["Goblin"].Stats.HP)
    let heroDamage = (100 - updated["Alice"].Stats.HP) + (100 - updated["Bob"].Stats.HP)
    Assert.Equal(10, heroDamage)

[<Fact>]
let ``applyAction NamedGroup with no matching Side is a no-op`` () =
    let state =
        [ actor "Alice" 100 20 5 "Heroes"
          actor "Bob"   100 20 5 "Heroes" ]
        |> Map.ofList
    let updated =
        applyAction state { Actor = "Alice"; Action = Attack(TargetSelector(Weakest, NamedGroup "Villains")) }
    Assert.Equal(100, updated["Alice"].Stats.HP)
    Assert.Equal(100, updated["Bob"].Stats.HP)

[<Fact>]
let ``team decl followed by named group targeting works end to end`` () =
    let state =
        [ actor "Alice"  100 20 5 "unset"
          actor "Goblin"  60 15 3 "unset" ]
        |> Map.ofList
    let state2 = applyStatement state  (STeamDecl("Heroes",   ["Alice"]))
    let state3 = applyStatement state2 (STeamDecl("Villains", ["Goblin"]))
    let state4 = applyStatement state3 (SAction { Actor = "Alice"; Action = Attack(TargetSelector(Weakest, NamedGroup "Villains")) })
    // damage = max 1 (20 - 3) = 17
    Assert.Equal(60 - 17, state4["Goblin"].Stats.HP)
