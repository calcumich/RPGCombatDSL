module Engine
open Types

let applyAction (characters: Map<string, Character>) (turn: Turn) : Map<string, Character> =
    let attacker = characters.[turn.Actor]
    match turn.Action with
    | Attack targetName when characters.ContainsKey(targetName) ->
        let target = characters.[targetName]
        let damage = max 1 (attacker.Stats.Attack - target.Stats.Defense)
        let updatedTarget = { target with Stats = { target.Stats with HP = target.Stats.HP - damage } }
        characters.Add(targetName, updatedTarget)
    | _ -> characters 
