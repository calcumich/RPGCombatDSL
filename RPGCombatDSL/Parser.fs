module Parser

open Types
open Lexer

type ParseError = {
    LineNumber: int
    LineText: string
    Message: string
}

type ParseResult = {
    Statements: Statement list
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

/// Accept either a bare identifier or a quoted string as a name. Quoted
/// strings let scripts use multi-word names like "Cave Troll".
let private expectIdent (c: Cursor) (whatFor: string) : Result<string, ParseError> =
    let t = peek c
    match t.Token with
    | TIdent name
    | TString name ->
        advance c
        Ok name
    | _ ->
        Error (err c t.Line (sprintf "Expected %s" whatFor))

let private parseModifier (word: string) : Modifier option =
    match word.ToLowerInvariant() with
    | "weakest" | "lowest"    -> Some Weakest
    | "strongest" | "highest" -> Some Strongest
    | "random"                -> Some Random
    | _                       -> None

let private parseGroup (word: string) : Group option =
    match word.ToLowerInvariant() with
    | "enemy" | "enemies" -> Some EnemyGroup
    | "ally"  | "allies"  -> Some AllyGroup
    | "any"               -> Some AnyGroup
    | _                   -> None

let private parseTarget (c: Cursor) (whatFor: string) : Result<TargetSpec, ParseError> =
    let t = peek c
    match t.Token with
    | TIdent word | TString word ->
        match parseModifier word with
        | Some modifier ->
            advance c
            let group =
                match (peek c).Token with
                | TIdent g | TString g ->
                    match parseGroup g with
                    | Some grp -> advance c; grp
                    | None     -> advance c; NamedGroup g
                | _ -> AnyGroup
            Ok (TargetSelector(modifier, group))
        | None ->
            advance c
            Ok (NamedTarget word)
    | _ ->
        Error (err c t.Line (sprintf "Expected %s" whatFor))

let private parseExpr (c: Cursor) : Result<Expr, ParseError> =
    let t = peek c
    match t.Token with
    | TInt n ->
        advance c
        Ok (EIntLit n)
    | TIdent name
    | TString name ->
        advance c
        match (peek c).Token with
        | TDot ->
            advance c
            let fieldTok = peek c
            match fieldTok.Token with
            | TIdent "HP"      -> advance c; Ok (EStatRef(name, StatField.HP))
            | TIdent "Attack"  -> advance c; Ok (EStatRef(name, StatField.Attack))
            | TIdent "Defense" -> advance c; Ok (EStatRef(name, StatField.Defense))
            | _ -> Error (err c fieldTok.Line "Expected HP, Attack, or Defense after '.'")
        | _ ->
            Error (err c t.Line "Expected '.field' (e.g. Bob.HP) in condition")
    | _ ->
        Error (err c t.Line "Expected a number or character.field in condition")

let private parseComparator (c: Cursor) : Result<Comparator, ParseError> =
    let t = peek c
    match t.Token with
    | TLt   -> advance c; Ok Lt
    | TLe   -> advance c; Ok Le
    | TGt   -> advance c; Ok Gt
    | TGe   -> advance c; Ok Ge
    | TEqEq -> advance c; Ok Eq
    | TNeq  -> advance c; Ok Ne
    | _ ->
        Error (err c t.Line "Expected a comparison operator (<, <=, >, >=, ==, !=)")

let private parseCondition (c: Cursor) : Result<Condition, ParseError> =
    parseExpr c
    |> Result.bind (fun lhs ->
        parseComparator c
        |> Result.bind (fun op ->
            parseExpr c
            |> Result.map (fun rhs -> Compare(lhs, op, rhs))))

let private parseAction (c: Cursor) : Result<Action, ParseError> =
    let t = peek c
    match t.Token with
    | TAttacks ->
        advance c
        parseTarget c "target name after 'attacks'" |> Result.map Attack
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
                parseTarget c "target name after 'on'"
                |> Result.map (fun target -> CastSpell(spell, target))
            | _ ->
                Ok (CastSpell(spell, NamedTarget ""))
    | _ ->
        Error (err c t.Line "Expected an action (attacks, defends, uses, casts)")

let private parseTurn (c: Cursor) : Result<Turn, ParseError> =
    let startTok = peek c
    match startTok.Token with
    | TIdent actor
    | TString actor ->
        advance c
        parseAction c
        |> Result.map (fun action -> { Actor = actor; Action = action })
    | _ ->
        Error (err c startTok.Line "Expected an actor name at start of line")

let private expectToken (c: Cursor) (expected: Token) (message: string) : Result<unit, ParseError> =
    let t = peek c
    if t.Token = expected then advance c; Ok ()
    else Error (err c t.Line message)

let rec private parseStatement (c: Cursor) : Result<Statement, ParseError> =
    match (peek c).Token with
    | TIf     -> parseIf c
    | TTeam   -> parseTeam c
    | TRepeat -> parseRepeat c
    | _       -> parseTurn c |> Result.map SAction

and private parseIf (c: Cursor) : Result<Statement, ParseError> =
    advance c  // consume TIf
    parseCondition c
    |> Result.bind (fun cond ->
        expectToken c TThen "Expected 'then' after condition"
        |> Result.bind (fun () ->
            parseStatement c
            |> Result.bind (fun thn ->
                match (peek c).Token with
                | TElse ->
                    advance c
                    parseStatement c
                    |> Result.map (fun els -> SIf(cond, thn, Some els))
                | _ ->
                    Ok (SIf(cond, thn, None)))))

and private parseTeam (c: Cursor) : Result<Statement, ParseError> =
    advance c  // consume TTeam
    expectIdent c "team name after 'team'"
    |> Result.bind (fun teamName ->
        expectToken c TLBrace "Expected '{' after team name"
        |> Result.bind (fun () ->
            let rec parseMembers acc =
                match (peek c).Token with
                | TRBrace ->
                    advance c
                    Ok (STeamDecl(teamName, List.rev acc))
                | TSemicolon | TNewline ->
                    advance c
                    parseMembers acc
                | TIdent name | TString name ->
                    advance c
                    parseMembers (name :: acc)
                | _ ->
                    let t = peek c
                    Error (err c t.Line "Expected member name or '}' in team block")
            parseMembers []))

and private parseBlock (c: Cursor) : Result<Statement list, ParseError> =
    expectToken c TLBrace "Expected '{'"
    |> Result.bind (fun () ->
        let stmts = System.Collections.Generic.List<Statement>()
        let rec loop () =
            match (peek c).Token with
            | TRBrace -> advance c; Ok (List.ofSeq stmts)
            | TEof    -> Error (err c (peek c).Line "Unexpected end of file inside block")
            | TNewline -> advance c; loop ()
            | _ ->
                match parseStatement c with
                | Error e -> Error e
                | Ok stmt ->
                    stmts.Add stmt
                    match (peek c).Token with
                    | TNewline -> advance c
                    | _ -> ()
                    loop ()
        loop ())

and private parseRepeat (c: Cursor) : Result<Statement, ParseError> =
    advance c  // consume TRepeat
    let t = peek c
    match t.Token with
    | TInt n ->
        advance c
        parseBlock c |> Result.map (fun body -> SRepeat(n, body))
    | _ ->
        Error (err c t.Line "Expected integer count after 'repeat'")

let parseScript (script: string) : ParseResult =
    let tokens = tokenize script |> List.toArray
    let cursor = { Pos = 0; Tokens = tokens; Lines = sourceLines script }

    let statements = System.Collections.Generic.List<Statement>()
    let errors = System.Collections.Generic.List<ParseError>()

    let rec loop () =
        match (peek cursor).Token with
        | TEof -> ()
        | TNewline ->
            advance cursor
            loop ()
        | _ ->
            match parseStatement cursor with
            | Ok stmt ->
                statements.Add stmt
                match (peek cursor).Token with
                | TEof -> ()
                | TNewline -> advance cursor
                | _ ->
                    let t = peek cursor
                    errors.Add (err cursor t.Line "Unexpected tokens after statement")
                    recoverToNextLine cursor
            | Error e ->
                errors.Add e
                recoverToNextLine cursor
            loop ()

    loop ()
    { Statements = List.ofSeq statements; Errors = List.ofSeq errors }

/// Test-only entry point: parse a single condition string in isolation.
let parseConditionString (text: string) : Result<Condition, ParseError> =
    let tokens = tokenize text |> List.toArray
    let cursor = { Pos = 0; Tokens = tokens; Lines = sourceLines text }
    parseCondition cursor

let parseStatements (script: string) : Result<Statement list, ParseError list> =
    let result = parseScript script
    match result.Errors with
    | [] -> Ok result.Statements
    | errors -> Error errors
