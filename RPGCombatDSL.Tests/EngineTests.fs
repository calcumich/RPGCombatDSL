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
