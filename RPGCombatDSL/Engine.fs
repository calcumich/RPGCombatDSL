module Engine
open Types

/// Apply a single turn to the current character map.
/// The function returns a new map with any updated characters.
let applyAction (characters: Map<string, Character>) (turn: Turn) : Map<string, Character> =
    let actor = characters.[turn.Actor]
    match turn.Action with
    | Attack targetName when characters.ContainsKey targetName ->
        let target = characters.[targetName]
        // Basic damage calculation. Target's defense mitigates damage.
        let damage = max 1 (actor.Stats.Attack - target.Stats.Defense)
        let updatedTarget = { target with Stats = { target.Stats with HP = target.Stats.HP - damage } }
        characters |> Map.add targetName updatedTarget
    | Defend ->
        // Increase the actor's defense as a simple representation of defending.
        let updatedActor = { actor with Stats = { actor.Stats with Defense = actor.Stats.Defense + 5 } }
        characters |> Map.add turn.Actor updatedActor
    | UseItem itemName ->
        // Very small item system used for demonstration purposes.
        // Different item names apply different effects.
        let updatedActor =
            match itemName.ToLowerInvariant() with
            | "healthpotion" -> { actor with Stats = { actor.Stats with HP = actor.Stats.HP + 20 } }
            | "powerpotion" -> { actor with Stats = { actor.Stats with Attack = actor.Stats.Attack + 5 } }
            | "defensepotion" -> { actor with Stats = { actor.Stats with Defense = actor.Stats.Defense + 5 } }
            | _ -> actor
        characters |> Map.add turn.Actor updatedActor
    | CastSpell(spellName, targetName) when characters.ContainsKey targetName ->
        // Spells can either heal or damage depending on their name.
        let target = characters.[targetName]
        let updatedTarget =
            match spellName.ToLowerInvariant() with
            | "heal" -> { target with Stats = { target.Stats with HP = target.Stats.HP + 15 } }
            | "fireball" ->
                let damage = max 1 (30 - target.Stats.Defense)
                { target with Stats = { target.Stats with HP = target.Stats.HP - damage } }
            | _ -> target
        characters |> Map.add targetName updatedTarget
    | _ -> characters
