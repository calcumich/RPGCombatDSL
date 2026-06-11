open Types
open Engine
open Parser

let alice = { Name = "Alice";  Stats = { HP = 100; Attack = 20; Defense = 10 }; Side = "heroes" }
let bob   = { Name = "Bob";    Stats = { HP = 90;  Attack = 15; Defense = 5  }; Side = "villains" }
let cleric = { Name = "Cleric"; Stats = { HP = 70;  Attack = 8;  Defense = 8  }; Side = "heroes" }

let characters = [ alice; bob; cleric ]

let behaviorScripts =
    [
        alice.Name, """
if Alice.HP < 25 then Alice uses HealthPotion
Alice attacks weakest enemy
"""
        bob.Name, """
if Bob.HP < 35 then Bob uses HealthPotion
Bob casts Fireball on weakest enemy
"""
        cleric.Name, """
if Alice.HP < 65 then Cleric casts Heal on lowest ally
Cleric attacks weakest enemy
"""
    ]

let parseBehaviorScripts scripts =
    let folder result (name, scriptText) =
        match result, parseStatements scriptText with
        | Error errors, Error parseErrors ->
            Error (errors @ (parseErrors |> List.map (fun e -> name, e)))
        | Error errors, Ok _ ->
            Error errors
        | Ok parsed, Error parseErrors ->
            Error (parseErrors |> List.map (fun e -> name, e))
        | Ok parsed, Ok statements ->
            Ok (Map.add name statements parsed)

    List.fold folder (Ok Map.empty) scripts

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
