module EngineTests

open Xunit
open Types
open Engine

let createChar name hp atk def = { Name = name; Stats = { HP = hp; Attack = atk; Defense = def } }

[<Fact>]
let ``attack action damages target`` () =
    let attacker = createChar "A" 100 20 5
    let target = createChar "B" 50 10 2
    let chars = [ attacker.Name, attacker; target.Name, target ] |> Map.ofList
    let turn = { Actor = "A"; Action = Attack "B" }
    let result = applyAction chars turn
    let updated = result.["B"]
    Assert.Equal(32, updated.Stats.HP)

[<Fact>]
let ``defend increases defense`` () =
    let actor = createChar "A" 100 20 5
    let chars = [ actor.Name, actor ] |> Map.ofList
    let turn = { Actor = "A"; Action = Defend }
    let result = applyAction chars turn
    Assert.Equal(10, result.["A"].Stats.Defense)

[<Fact>]
let ``invalid attack leaves state unchanged`` () =
    let actor = createChar "A" 100 20 5
    let chars = [ actor.Name, actor ] |> Map.ofList
    let turn = { Actor = "A"; Action = Attack "Unknown" }
    let result = applyAction chars turn
    Assert.Equal(chars, result)

