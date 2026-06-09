module EngineTests

open Engine
open Types
open Xunit

let private actor name hp attack defense =
    name,
    {
        Name = name
        Stats = { HP = hp; Attack = attack; Defense = defense }
    }

[<Fact>]
let ``attack action damages target`` () =
    let characters =
        [ actor "Alice" 100 20 5; actor "Bob" 50 10 2 ]
        |> Map.ofList

    let updated =
        applyAction characters { Actor = "Alice"; Action = Attack "Bob" }

    Assert.Equal(32, updated["Bob"].Stats.HP)

[<Fact>]
let ``defend increases defense`` () =
    let characters =
        [ actor "Alice" 100 20 5 ]
        |> Map.ofList

    let updated =
        applyAction characters { Actor = "Alice"; Action = Defend }

    Assert.Equal(10, updated["Alice"].Stats.Defense)

[<Fact>]
let ``invalid attack leaves state unchanged`` () =
    let characters =
        [ actor "Alice" 100 20 5 ]
        |> Map.ofList

    let updated =
        applyAction characters { Actor = "Alice"; Action = Attack "Unknown" }

    Assert.Equal<Map<string, Character>>(characters, updated)

[<Fact>]
let ``attack always deals at least one damage`` () =
    let characters =
        [ actor "Bob" 90 15 5; actor "Tank" 40 3 100 ]
        |> Map.ofList

    let updated =
        applyAction characters { Actor = "Bob"; Action = Attack "Tank" }

    Assert.Equal(39, updated["Tank"].Stats.HP)

[<Fact>]
let ``if condition true applies then branch`` () =
    let characters = [ actor "Bob" 20 10 5 ] |> Map.ofList
    let stmt =
        SIf(
            Compare(EStatRef("Bob", StatField.HP), Lt, EIntLit 30),
            SAction { Actor = "Bob"; Action = UseItem "HealthPotion" },
            None)

    let updated = applyStatement characters stmt

    Assert.Equal(40, updated["Bob"].Stats.HP)

[<Fact>]
let ``if condition false with no else leaves state unchanged`` () =
    let characters = [ actor "Bob" 80 10 5 ] |> Map.ofList
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
        [ actor "Alice" 80 20 5; actor "Bob" 50 10 5 ] |> Map.ofList
    let stmt =
        SIf(
            Compare(EStatRef("Alice", StatField.HP), Lt, EIntLit 30),
            SAction { Actor = "Alice"; Action = Defend },
            Some (SAction { Actor = "Alice"; Action = Attack "Bob" }))

    let updated = applyStatement characters stmt

    // Alice.HP (80) is not < 30, so else branch runs: Alice attacks Bob.
    // Damage = max 1 (20 - 5) = 15, so Bob.HP = 50 - 15 = 35.
    Assert.Equal(35, updated["Bob"].Stats.HP)
    Assert.Equal(5, updated["Alice"].Stats.Defense)

[<Fact>]
let ``evalExpr resolves stat reference via condition`` () =
    let characters = [ actor "Bob" 42 10 5 ] |> Map.ofList
    let stmt =
        SIf(
            Compare(EStatRef("Bob", StatField.HP), Eq, EIntLit 42),
            SAction { Actor = "Bob"; Action = Defend },
            None)

    let updated = applyStatement characters stmt

    Assert.Equal(10, updated["Bob"].Stats.Defense)
