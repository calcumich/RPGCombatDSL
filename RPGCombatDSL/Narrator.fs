module Narrator

open Types

let render (trace: BattleEvent list) : string =
    trace
    |> List.map (function
        | RoundStarted round        -> sprintf "--- Round %d ---" round
        | TurnTaken _               -> ""
        | DamageDealt(a, t, n)      -> sprintf "%s strikes %s for %d damage." a t n
        | HealApplied(src, t, n)    -> sprintf "%s restores %d HP to %s." src n t
        | StatBoosted(a, f, n)      -> sprintf "%s gains +%d %A." a n f
        | TargetMissed(a, reason)   -> sprintf "%s finds no target (%s)." a reason
        | CharDefeated name         -> sprintf "%s has been defeated!" name
        | BattleEnded(Winner s, r)  -> sprintf "%s wins after %d rounds." s r
        | BattleEnded(Draw, r)      -> sprintf "Draw after %d rounds." r)
    |> List.filter (fun s -> s <> "")
    |> String.concat "\n"
