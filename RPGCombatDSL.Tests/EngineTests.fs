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

let private characters =
    [ actor "Alice" 100 20 10; actor "Bob" 90 15 5 ]
    |> Map.ofList

[<Fact>]
let ``applyAction attack lowers target hp by mitigated damage`` () =
    let updated =
        applyAction characters { Actor = "Alice"; Action = Attack "Bob" }

    Assert.Equal(75, updated["Bob"].Stats.HP)

[<Fact>]
let ``applyAction attack always deals at least one damage`` () =
    let tank =
        {
            Name = "Tank"
            Stats = { HP = 40; Attack = 3; Defense = 100 }
        }

    let updated =
        applyAction
            (characters |> Map.add tank.Name tank)
            { Actor = "Bob"; Action = Attack "Tank" }

    Assert.Equal(39, updated["Tank"].Stats.HP)

[<Fact>]
let ``applyAction leaves state unchanged for defend`` () =
    let updated = applyAction characters { Actor = "Bob"; Action = Defend }

    Assert.Equal<Map<string, Character>>(characters, updated)
