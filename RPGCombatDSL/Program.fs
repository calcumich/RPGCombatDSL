open Types
open Engine
open Parser

let alice = {
    Name = "Alice"
    Stats = { HP = 100; Attack = 20; Defense = 10 }
}

let bob = {
    Name = "Bob"
    Stats = { HP = 90; Attack = 15; Defense = 5 }
}

let characters = [ alice.Name, alice; bob.Name, bob ] |> Map.ofList

let scriptText = """
Alice attacks Bob
Bob defends
"""

let script = parseTurns scriptText

let simulateBattle turns =
    let finalState = List.fold applyAction characters turns
    for KeyValue(name, char) in finalState do
        printfn "%s - HP: %d" name char.Stats.HP

[<EntryPoint>]
let main _ =
    simulateBattle script
    0
