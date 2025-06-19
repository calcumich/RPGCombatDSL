open Types
open Engine

let alice = {
    Name = "Alice"
    Stats = { HP = 100; Attack = 20; Defense = 10 }
}

let bob = {
    Name = "Bob"
    Stats = { HP = 90; Attack = 15; Defense = 5 }
}

let characters = [ alice.Name, alice; bob.Name, bob ] |> Map.ofList

let script = [
    { Actor = "Alice"; Action = Attack "Bob" }
    { Actor = "Bob"; Action = Attack "Alice" }
]

let simulateBattle turns =
    let finalState = List.fold applyAction characters turns
    for KeyValue(name, char) in finalState do
        printfn "%s - HP: %d" name char.Stats.HP

[<EntryPoint>]
let main _ =
    simulateBattle script
    0