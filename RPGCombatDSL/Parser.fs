module Parser

open System
open Types

type ParseError = {
    LineNumber: int
    LineText: string
    Message: string
}

type ParseResult = {
    Turns: Turn list
    Errors: ParseError list
}

/// Parse a single line of the DSL into either a Turn or an error.
let private parseLine (lineNumber: int) (line: string) : Result<Turn, ParseError> =
    let words =
        line.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList

    match words with
    | [ actor; "attacks"; target ] ->
        Ok { Actor = actor; Action = Attack target }
    | [ actor; "defends" ] ->
        Ok { Actor = actor; Action = Defend }
    | _ ->
        Error {
            LineNumber = lineNumber
            LineText = line
            Message = "Unsupported command"
        }

/// Parse a script consisting of one command per line into turns and parse errors.
let parseScript (script: string) : ParseResult =
    let folder state (lineNumber, line) =
        match parseLine lineNumber line with
        | Ok turn ->
            { state with Turns = turn :: state.Turns }
        | Error error ->
            { state with Errors = error :: state.Errors }

    script.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
    |> Array.mapi (fun index line -> index + 1, line.Trim())
    |> Array.fold folder { Turns = []; Errors = [] }
    |> fun result ->
        {
            Turns = List.rev result.Turns
            Errors = List.rev result.Errors
        }

/// Parse a script and fail when any line is invalid.
let parseTurns (script: string) : Result<Turn list, ParseError list> =
    let result = parseScript script

    match result.Errors with
    | [] -> Ok result.Turns
    | errors -> Error errors
