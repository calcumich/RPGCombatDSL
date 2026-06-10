module Engine
open Types

let private resolveTarget
        (characters: Map<string, Character>)
        (actorName: string)
        (spec: TargetSpec) : string option =
    match spec with
    | NamedTarget name ->
        if characters.ContainsKey name then Some name else None
    | TargetSelector(modifier, group) ->
        let actorSide =
            characters |> Map.tryFind actorName |> Option.map (fun ch -> ch.Side) |> Option.defaultValue ""
        let candidates =
            characters
            |> Map.toSeq
            |> Seq.map snd
            |> Seq.filter (fun ch ->
                match group with
                | EnemyGroup      -> ch.Side <> actorSide
                | AllyGroup       -> ch.Side = actorSide
                | AnyGroup        -> true
                | NamedGroup name -> ch.Side = name)
            |> Seq.toList
        if candidates.IsEmpty then None
        else
            match modifier with
            | Weakest   -> candidates |> List.minBy (fun ch -> ch.Stats.HP) |> fun ch -> Some ch.Name
            | Strongest -> candidates |> List.maxBy (fun ch -> ch.Stats.HP) |> fun ch -> Some ch.Name
            | Random    ->
                let idx = System.Random.Shared.Next(candidates.Length)
                Some candidates.[idx].Name

let applyAction (characters: Map<string, Character>) (turn: Turn) : Map<string, Character> =
    match Map.tryFind turn.Actor characters with
    | None -> characters
    | Some actor ->
    match turn.Action with
    | Attack spec ->
        match resolveTarget characters turn.Actor spec with
        | None -> characters
        | Some targetName ->
            let target = characters.[targetName]
            let damage = max 1 (actor.Stats.Attack - target.Stats.Defense)
            let updatedTarget = { target with Stats = { target.Stats with HP = target.Stats.HP - damage } }
            characters |> Map.add targetName updatedTarget
    | Defend ->
        let updatedActor = { actor with Stats = { actor.Stats with Defense = actor.Stats.Defense + 5 } }
        characters |> Map.add turn.Actor updatedActor
    | UseItem itemName ->
        let updatedActor =
            match itemName.ToLowerInvariant() with
            | "healthpotion" -> { actor with Stats = { actor.Stats with HP = actor.Stats.HP + 20 } }
            | "powerpotion"  -> { actor with Stats = { actor.Stats with Attack = actor.Stats.Attack + 5 } }
            | "defensepotion" -> { actor with Stats = { actor.Stats with Defense = actor.Stats.Defense + 5 } }
            | _ -> actor
        characters |> Map.add turn.Actor updatedActor
    | CastSpell(spellName, spec) ->
        match resolveTarget characters turn.Actor spec with
        | None -> characters
        | Some targetName ->
            let target = characters.[targetName]
            let updatedTarget =
                match spellName.ToLowerInvariant() with
                | "heal" -> { target with Stats = { target.Stats with HP = target.Stats.HP + 15 } }
                | "fireball" ->
                    let damage = max 1 (30 - target.Stats.Defense)
                    { target with Stats = { target.Stats with HP = target.Stats.HP - damage } }
                | _ -> target
            characters |> Map.add targetName updatedTarget

let private getStat (characters: Map<string, Character>) (name: string) (field: StatField) : int =
    let s = characters.[name].Stats
    match field with
    | StatField.HP      -> s.HP
    | StatField.Attack  -> s.Attack
    | StatField.Defense -> s.Defense

let private evalExpr (characters: Map<string, Character>) (expr: Expr) : int =
    match expr with
    | EIntLit n -> n
    | EStatRef(name, field) -> getStat characters name field

let private evalCondition (characters: Map<string, Character>) (Compare(lhs, op, rhs)) : bool =
    let lv = evalExpr characters lhs
    let rv = evalExpr characters rhs
    match op with
    | Lt -> lv < rv
    | Le -> lv <= rv
    | Gt -> lv > rv
    | Ge -> lv >= rv
    | Eq -> lv = rv
    | Ne -> lv <> rv

/// Apply a top-level statement. Conditionals are evaluated against the current
/// character map; the chosen branch (or no branch) is then applied recursively.
let rec applyStatement (characters: Map<string, Character>) (stmt: Statement) : Map<string, Character> =
    match stmt with
    | SAction turn -> applyAction characters turn
    | SIf(cond, thenBranch, elseBranch) ->
        if evalCondition characters cond then
            applyStatement characters thenBranch
        else
            match elseBranch with
            | Some s -> applyStatement characters s
            | None -> characters
    | STeamDecl(teamName, members) ->
        members
        |> List.fold (fun state name ->
            match Map.tryFind name state with
            | Some ch -> Map.add name { ch with Side = teamName } state
            | None    -> state) characters
    | SRepeat(count, body) ->
        [1..count]
        |> List.fold (fun state _ -> List.fold applyStatement state body) characters
