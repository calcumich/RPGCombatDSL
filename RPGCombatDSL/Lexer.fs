module Lexer

type Token =
    | TIdent of string
    | TAttacks
    | TDefends
    | TUses
    | TCasts
    | TOn
    | TNewline
    | TEof

type PositionedToken = { Token: Token; Line: int }

let private isIdentStart c = System.Char.IsLetter c || c = '_'
let private isIdentCont c = System.Char.IsLetterOrDigit c || c = '_'

let private keywordOrIdent (s: string) =
    match s with
    | "attacks" -> TAttacks
    | "defends" -> TDefends
    | "uses" -> TUses
    | "casts" -> TCasts
    | "on" -> TOn
    | _ -> TIdent s

let tokenize (source: string) : PositionedToken list =
    let tokens = System.Collections.Generic.List<PositionedToken>()
    let mutable i = 0
    let mutable line = 1
    let len = source.Length

    let emit tok = tokens.Add { Token = tok; Line = line }

    while i < len do
        let c = source.[i]
        if c = '\n' then
            emit TNewline
            line <- line + 1
            i <- i + 1
        elif c = '\r' then
            emit TNewline
            line <- line + 1
            i <- i + 1
            if i < len && source.[i] = '\n' then i <- i + 1
        elif c = ' ' || c = '\t' then
            i <- i + 1
        elif isIdentStart c then
            let start = i
            i <- i + 1
            while i < len && isIdentCont source.[i] do
                i <- i + 1
            emit (keywordOrIdent (source.Substring(start, i - start)))
        else
            // Unknown character: emit as a single-char identifier so the parser
            // reports a meaningful error on the offending line.
            emit (TIdent (string c))
            i <- i + 1

    emit TEof
    List.ofSeq tokens

/// Split the original source into a 1-indexed array of lines so error messages
/// can quote the offending line verbatim.
let sourceLines (source: string) : string[] =
    source.Split([| '\n' |])
    |> Array.map (fun l -> l.TrimEnd('\r'))
