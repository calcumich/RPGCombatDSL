module Parser

open System
open Types

/// Parse a single line of the DSL into a Turn option
let private parseLine (line: string) : Turn option =
    let words =
        line.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList

    match words with
    | [ actor; "attacks"; target ] ->
        Some { Actor = actor; Action = Attack target }
    | [ actor; "defends" ] ->
        Some { Actor = actor; Action = Defend }
    | _ -> None

/// Parse a script consisting of one command per line into a list of Turns
let parseTurns (script: string) : Turn list =
    script.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.choose parseLine
    |> Array.toList
