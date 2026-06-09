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
# A small demo script exercising every syntax form.
Alice attacks Bob
Bob defends
if Bob.HP < 80 then Bob uses HealthPotion
Alice uses HealthPotion
Bob casts Fireball on Alice
if Alice.HP < 50 then Alice defends else Alice attacks Bob
"""

let simulateBattle statements =
    let finalState = List.fold applyStatement characters statements
    for KeyValue(name, char) in finalState do
        printfn "%s - HP: %d" name char.Stats.HP

[<EntryPoint>]
let main _ =
    match parseStatements scriptText with
    | Ok script ->
        simulateBattle script
        0
    | Error errors ->
        for error in errors do
            printfn "Parse error on line %d: %s (%s)" error.LineNumber error.Message error.LineText

        1
