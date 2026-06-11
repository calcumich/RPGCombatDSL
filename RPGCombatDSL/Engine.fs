module Engine
open Types

let defaultBattleConfig = { MaxRounds = 100 }

let private resolveTarget
        (includeDefeated: bool)
        (characters: Map<string, Character>)
        (actorName: string)
        (spec: TargetSpec) : string option =
    let isEligible (ch: Character) =
        includeDefeated || ch.Stats.HP > 0

    match spec with
    | NamedTarget name ->
        match Map.tryFind name characters with
        | Some ch when isEligible ch -> Some name
        | _ -> None
    | TargetSelector(modifier, group) ->
        let actorSide =
            characters |> Map.tryFind actorName |> Option.map (fun ch -> ch.Side) |> Option.defaultValue ""
        let candidates =
            characters
            |> Map.toSeq
            |> Seq.map snd
            |> Seq.filter isEligible
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

let private applyActionWithTargeting
        (includeDefeated: bool)
        (characters: Map<string, Character>)
        (turn: Turn) : Map<string, Character> * BattleEvent list =
    match Map.tryFind turn.Actor characters with
    | None -> characters, []
    | Some actor ->
    let turnEvent = TurnTaken(turn.Actor, turn.Action)
    match turn.Action with
    | Attack spec ->
        match resolveTarget includeDefeated characters turn.Actor spec with
        | None ->
            characters, [turnEvent; TargetMissed(turn.Actor, "no eligible target")]
        | Some targetName ->
            let target = characters.[targetName]
            let damage = max 1 (actor.Stats.Attack - target.Stats.Defense)
            let updatedTarget = { target with Stats = { target.Stats with HP = target.Stats.HP - damage } }
            let newState = characters |> Map.add targetName updatedTarget
            let defeated = if updatedTarget.Stats.HP <= 0 then [CharDefeated targetName] else []
            newState, [turnEvent; DamageDealt(turn.Actor, targetName, damage)] @ defeated
    | Defend ->
        let updatedActor = { actor with Stats = { actor.Stats with Defense = actor.Stats.Defense + 5 } }
        characters |> Map.add turn.Actor updatedActor,
        [turnEvent; StatBoosted(turn.Actor, StatField.Defense, 5)]
    | UseItem itemName ->
        match itemName.ToLowerInvariant() with
        | "healthpotion" ->
            let updated = { actor with Stats = { actor.Stats with HP = actor.Stats.HP + 20 } }
            characters |> Map.add turn.Actor updated,
            [turnEvent; HealApplied(turn.Actor, turn.Actor, 20)]
        | "powerpotion" ->
            let updated = { actor with Stats = { actor.Stats with Attack = actor.Stats.Attack + 5 } }
            characters |> Map.add turn.Actor updated,
            [turnEvent; StatBoosted(turn.Actor, StatField.Attack, 5)]
        | "defensepotion" ->
            let updated = { actor with Stats = { actor.Stats with Defense = actor.Stats.Defense + 5 } }
            characters |> Map.add turn.Actor updated,
            [turnEvent; StatBoosted(turn.Actor, StatField.Defense, 5)]
        | _ ->
            characters |> Map.add turn.Actor actor, [turnEvent]
    | CastSpell(spellName, spec) ->
        match resolveTarget includeDefeated characters turn.Actor spec with
        | None ->
            characters, [turnEvent; TargetMissed(turn.Actor, "no eligible target")]
        | Some targetName ->
            let target = characters.[targetName]
            match spellName.ToLowerInvariant() with
            | "heal" ->
                let updated = { target with Stats = { target.Stats with HP = target.Stats.HP + 15 } }
                characters |> Map.add targetName updated,
                [turnEvent; HealApplied(turn.Actor, targetName, 15)]
            | "fireball" ->
                let damage = max 1 (30 - target.Stats.Defense)
                let updated = { target with Stats = { target.Stats with HP = target.Stats.HP - damage } }
                let newState = characters |> Map.add targetName updated
                let defeated = if updated.Stats.HP <= 0 then [CharDefeated targetName] else []
                newState, [turnEvent; DamageDealt(turn.Actor, targetName, damage)] @ defeated
            | _ ->
                characters, [turnEvent]

let applyAction (characters: Map<string, Character>) (turn: Turn) : Map<string, Character> =
    applyActionWithTargeting true characters turn |> fst

let applyActionWithEvents (characters: Map<string, Character>) (turn: Turn) : Map<string, Character> * BattleEvent list =
    applyActionWithTargeting true characters turn

let private applyBattleAction (characters: Map<string, Character>) (turn: Turn) : Map<string, Character> * BattleEvent list =
    applyActionWithTargeting false characters turn

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

let rec private chooseFromStatement
        (actorName: string)
        (characters: Map<string, Character>)
        (stmt: Statement) : Result<Turn option, string> =
    match stmt with
    | SAction turn ->
        if turn.Actor = actorName then Ok (Some turn)
        else Error (sprintf "Behavior for '%s' contains an action for '%s'." actorName turn.Actor)
    | SIf(cond, thenBranch, elseBranch) ->
        if evalCondition characters cond then
            chooseFromStatement actorName characters thenBranch
        else
            match elseBranch with
            | Some branch -> chooseFromStatement actorName characters branch
            | None -> Ok None
    | STeamDecl _ ->
        Ok None
    | SRepeat(count, body) ->
        if count <= 0 then Ok None
        else chooseFromStatements actorName characters body

and private chooseFromStatements
        (actorName: string)
        (characters: Map<string, Character>)
        (statements: Statement list) : Result<Turn option, string> =
    match statements with
    | [] -> Ok None
    | stmt :: rest ->
        match chooseFromStatement actorName characters stmt with
        | Error message -> Error message
        | Ok (Some turn) -> Ok (Some turn)
        | Ok None -> chooseFromStatements actorName characters rest

let chooseBehaviorAction
        (actorName: string)
        (characters: Map<string, Character>)
        (statements: Statement list) : Result<Turn option, string> =
    chooseFromStatements actorName characters statements

let private livingSides (characters: Map<string, Character>) : string list =
    characters
    |> Map.toList
    |> List.choose (fun (_, ch) -> if ch.Stats.HP > 0 then Some ch.Side else None)
    |> List.distinct

let private battleOutcome (characters: Map<string, Character>) : BattleOutcome option =
    match livingSides characters with
    | [ side ] -> Some (Winner side)
    | [] -> Some Draw
    | _ -> None

let runBattle
        (config: BattleConfig)
        (initialOrder: Character list)
        (behaviorScripts: Map<string, Statement list>) : Result<BattleResult, BattleError list> =
    let maxRounds = max 0 config.MaxRounds
    let initialState = initialOrder |> List.map (fun ch -> ch.Name, ch) |> Map.ofList

    let missingScriptErrors =
        initialOrder
        |> List.choose (fun ch ->
            if behaviorScripts.ContainsKey ch.Name then None
            else Some { Character = ch.Name; Message = "Missing behavior script." })

    if not missingScriptErrors.IsEmpty then
        Error missingScriptErrors
    else
        let rec runRound round state =
            match battleOutcome state with
            | Some outcome ->
                Ok { Outcome = outcome; FinalState = state; RoundsCompleted = round; Trace = [] }
            | None when round >= maxRounds ->
                Ok { Outcome = Draw; FinalState = state; RoundsCompleted = round; Trace = [] }
            | None ->
                let folder resultState actor =
                    match resultState with
                    | Error _ as error -> error
                    | Ok state ->
                        match Map.tryFind actor.Name state with
                        | Some currentActor when currentActor.Stats.HP > 0 ->
                            let statements = behaviorScripts.[actor.Name]
                            match chooseBehaviorAction actor.Name state statements with
                            | Error message ->
                                Error [{ Character = actor.Name; Message = message }]
                            | Ok None -> Ok state
                            | Ok (Some turn) -> Ok (applyBattleAction state turn |> fst)
                        | _ -> Ok state

                match List.fold folder (Ok state) initialOrder with
                | Error errors -> Error errors
                | Ok nextState -> runRound (round + 1) nextState

        runRound 0 initialState
