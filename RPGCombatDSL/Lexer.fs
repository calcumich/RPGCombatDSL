module Lexer

type Token =
    | TIdent of string
    | TInt of int
    | TAttacks
    | TDefends
    | TUses
    | TCasts
    | TOn
    | TIf
    | TThen
    | TElse
    | TDot
    | TLt
    | TLe
    | TGt
    | TGe
    | TEqEq
    | TNeq
    | TString of string
    | TTeam
    | TRepeat
    | TLBrace
    | TRBrace
    | TSemicolon
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
    | "if"     -> TIf
    | "then"   -> TThen
    | "else"   -> TElse
    | "team"   -> TTeam
    | "repeat" -> TRepeat
    | _ -> TIdent s

let tokenize (source: string) : PositionedToken list =
    let tokens = System.Collections.Generic.List<PositionedToken>()
    let mutable i = 0
    let mutable line = 1
    let len = source.Length

    let emit tok = tokens.Add { Token = tok; Line = line }

    let peekNext () =
        if i + 1 < len then Some source.[i + 1] else None

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
        elif c = '#' then
            // Line comment: skip to end of line, but don't consume the newline
            // itself — the newline branch above will emit TNewline.
            while i < len && source.[i] <> '\n' && source.[i] <> '\r' do
                i <- i + 1
        elif c = '"' then
            let startLine = line
            i <- i + 1
            let start = i
            while i < len && source.[i] <> '"' && source.[i] <> '\n' do
                i <- i + 1
            if i < len && source.[i] = '"' then
                let value = source.Substring(start, i - start)
                tokens.Add { Token = TString value; Line = startLine }
                i <- i + 1
            else
                // Unterminated string: emit what we have so the parser can
                // surface a meaningful error on the offending line.
                let value = source.Substring(start, i - start)
                tokens.Add { Token = TString value; Line = startLine }
        elif isIdentStart c then
            let start = i
            i <- i + 1
            while i < len && isIdentCont source.[i] do
                i <- i + 1
            emit (keywordOrIdent (source.Substring(start, i - start)))
        elif System.Char.IsDigit c then
            let start = i
            i <- i + 1
            while i < len && System.Char.IsDigit source.[i] do
                i <- i + 1
            emit (TInt (System.Int32.Parse(source.Substring(start, i - start))))
        elif c = '.' then
            emit TDot
            i <- i + 1
        elif c = '<' then
            match peekNext () with
            | Some '=' -> emit TLe; i <- i + 2
            | _ -> emit TLt; i <- i + 1
        elif c = '>' then
            match peekNext () with
            | Some '=' -> emit TGe; i <- i + 2
            | _ -> emit TGt; i <- i + 1
        elif c = '=' then
            match peekNext () with
            | Some '=' -> emit TEqEq; i <- i + 2
            | _ ->
                emit (TIdent (string c))
                i <- i + 1
        elif c = '!' then
            match peekNext () with
            | Some '=' -> emit TNeq; i <- i + 2
            | _ ->
                emit (TIdent (string c))
                i <- i + 1
        elif c = '{' then emit TLBrace;    i <- i + 1
        elif c = '}' then emit TRBrace;    i <- i + 1
        elif c = ';' then emit TSemicolon; i <- i + 1
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
