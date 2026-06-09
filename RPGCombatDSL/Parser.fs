module Parser

open Types
open Lexer

type ParseError = {
    LineNumber: int
    LineText: string
    Message: string
}

type ParseResult = {
    Turns: Turn list
    Errors: ParseError list
}

type private Cursor = {
    mutable Pos: int
    Tokens: PositionedToken[]
    Lines: string[]
}

let private peek (c: Cursor) = c.Tokens.[c.Pos]

let private advance (c: Cursor) =
    if c.Pos < c.Tokens.Length - 1 then c.Pos <- c.Pos + 1

let private lineText (c: Cursor) lineNumber =
    let idx = lineNumber - 1
    if idx >= 0 && idx < c.Lines.Length then c.Lines.[idx].Trim() else ""

let private err (c: Cursor) line message =
    { LineNumber = line; LineText = lineText c line; Message = message }

let private recoverToNextLine (c: Cursor) =
    while (peek c).Token <> TNewline && (peek c).Token <> TEof do
        advance c
    if (peek c).Token = TNewline then advance c

let private expectIdent (c: Cursor) (whatFor: string) : Result<string, ParseError> =
    let t = peek c
    match t.Token with
    | TIdent name ->
        advance c
        Ok name
    | _ ->
        Error (err c t.Line (sprintf "Expected %s" whatFor))

let private parseAction (c: Cursor) : Result<Action, ParseError> =
    let t = peek c
    match t.Token with
    | TAttacks ->
        advance c
        expectIdent c "target name after 'attacks'" |> Result.map Attack
    | TDefends ->
        advance c
        Ok Defend
    | TUses ->
        advance c
        expectIdent c "item name after 'uses'" |> Result.map UseItem
    | TCasts ->
        advance c
        match expectIdent c "spell name after 'casts'" with
        | Error e -> Error e
        | Ok spell ->
            match (peek c).Token with
            | TOn ->
                advance c
                expectIdent c "target name after 'on'"
                |> Result.map (fun target -> CastSpell(spell, target))
            | _ ->
                Ok (CastSpell(spell, ""))
    | _ ->
        Error (err c t.Line "Expected an action (attacks, defends, uses, casts)")

let private parseTurn (c: Cursor) : Result<Turn, ParseError> =
    let startTok = peek c
    match startTok.Token with
    | TIdent actor ->
        advance c
        parseAction c
        |> Result.map (fun action -> { Actor = actor; Action = action })
    | _ ->
        Error (err c startTok.Line "Expected an actor name at start of line")

let parseScript (script: string) : ParseResult =
    let tokens = tokenize script |> List.toArray
    let cursor = { Pos = 0; Tokens = tokens; Lines = sourceLines script }

    let turns = System.Collections.Generic.List<Turn>()
    let errors = System.Collections.Generic.List<ParseError>()

    let rec loop () =
        match (peek cursor).Token with
        | TEof -> ()
        | TNewline ->
            advance cursor
            loop ()
        | _ ->
            match parseTurn cursor with
            | Ok turn ->
                turns.Add turn
                match (peek cursor).Token with
                | TEof -> ()
                | TNewline -> advance cursor
                | _ ->
                    let t = peek cursor
                    errors.Add (err cursor t.Line "Unexpected tokens after turn")
                    recoverToNextLine cursor
            | Error e ->
                errors.Add e
                recoverToNextLine cursor
            loop ()

    loop ()
    { Turns = List.ofSeq turns; Errors = List.ofSeq errors }

let parseTurns (script: string) : Result<Turn list, ParseError list> =
    let result = parseScript script
    match result.Errors with
    | [] -> Ok result.Turns
    | errors -> Error errors
