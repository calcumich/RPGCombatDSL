module ParserTests

open Parser
open Types
open Xunit

[<Fact>]
let ``parseStatements parses supported commands in order`` () =
    let script = """
Alice attacks Bob
Bob defends
"""

    let statements = parseStatements script

    let expected =
        [
            SAction { Actor = "Alice"; Action = Attack (NamedTarget "Bob") }
            SAction { Actor = "Bob"; Action = Defend }
        ]

    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, statements)

[<Fact>]
let ``parseStatements parses uses and casts actions`` () =
    let script = """
Alice uses HealthPotion
Bob casts Fireball on Alice
Cleric casts Heal
"""

    let statements = parseStatements script

    let expected =
        [
            SAction { Actor = "Alice"; Action = UseItem "HealthPotion" }
            SAction { Actor = "Bob"; Action = CastSpell("Fireball", NamedTarget "Alice") }
            SAction { Actor = "Cleric"; Action = CastSpell("Heal", NamedTarget "") }
        ]

    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, statements)

[<Fact>]
let ``parseStatements returns an error list when an unknown action is used`` () =
    let script = """
Alice attacks Bob
Alice frobnicates Bob
"""

    let result = parseStatements script

    let expected =
        [
            {
                LineNumber = 3
                LineText = "Alice frobnicates Bob"
                Message = "Expected an action (attacks, defends, uses, casts)"
            }
        ]

    Assert.Equal<Result<Statement list, ParseError list>>(Error expected, result)

[<Fact>]
let ``parseScript returns both valid statements and invalid lines`` () =
    let script = """
Alice attacks Bob
Alice frobnicates Bob
Bob defends
"""

    let result = parseScript script

    let expectedStatements =
        [
            SAction { Actor = "Alice"; Action = Attack (NamedTarget "Bob") }
            SAction { Actor = "Bob"; Action = Defend }
        ]

    Assert.Equal<Statement list>(expectedStatements, result.Statements)
    Assert.Equal(1, result.Errors.Length)
    Assert.Equal(3, result.Errors.[0].LineNumber)
    Assert.Equal("Alice frobnicates Bob", result.Errors.[0].LineText)

[<Fact>]
let ``parseStatements reports missing target after attacks`` () =
    let script = "Alice attacks"

    let result = parseStatements script

    match result with
    | Error [ e ] ->
        Assert.Equal(1, e.LineNumber)
        Assert.Contains("target name after 'attacks'", e.Message)
    | other ->
        Assert.Fail(sprintf "Expected single error, got %A" other)

[<Fact>]
let ``parseCondition parses stat reference compared to literal`` () =
    let result = parseConditionString "Bob.HP < 30"
    let expected = Compare(EStatRef("Bob", StatField.HP), Lt, EIntLit 30)
    Assert.Equal<Result<Condition, ParseError>>(Ok expected, result)

[<Fact>]
let ``parseCondition parses literal compared to stat reference`` () =
    let result = parseConditionString "50 >= Alice.Attack"
    let expected = Compare(EIntLit 50, Ge, EStatRef("Alice", StatField.Attack))
    Assert.Equal<Result<Condition, ParseError>>(Ok expected, result)

[<Fact>]
let ``parseCondition parses equality and inequality`` () =
    let eq = parseConditionString "Bob.Defense == 5"
    let ne = parseConditionString "Bob.HP != 0"
    Assert.Equal<Result<Condition, ParseError>>(
        Ok (Compare(EStatRef("Bob", StatField.Defense), Eq, EIntLit 5)), eq)
    Assert.Equal<Result<Condition, ParseError>>(
        Ok (Compare(EStatRef("Bob", StatField.HP), Ne, EIntLit 0)), ne)

[<Fact>]
let ``parseStatements parses an if then statement`` () =
    let result = parseStatements "if Bob.HP < 30 then Bob uses HealthPotion"
    let expected =
        [
            SIf(
                Compare(EStatRef("Bob", StatField.HP), Lt, EIntLit 30),
                SAction { Actor = "Bob"; Action = UseItem "HealthPotion" },
                None)
        ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements parses if then else statement`` () =
    let result =
        parseStatements "if Alice.HP > 50 then Alice attacks Bob else Alice defends"
    let expected =
        [
            SIf(
                Compare(EStatRef("Alice", StatField.HP), Gt, EIntLit 50),
                SAction { Actor = "Alice"; Action = Attack (NamedTarget "Bob") },
                Some (SAction { Actor = "Alice"; Action = Defend }))
        ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements parses nested ifs`` () =
    let result =
        parseStatements "if Bob.HP < 30 then if Bob.Defense < 10 then Bob defends"
    let expected =
        [
            SIf(
                Compare(EStatRef("Bob", StatField.HP), Lt, EIntLit 30),
                SIf(
                    Compare(EStatRef("Bob", StatField.Defense), Lt, EIntLit 10),
                    SAction { Actor = "Bob"; Action = Defend },
                    None),
                None)
        ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements reports missing then`` () =
    match parseStatements "if Bob.HP < 30 Bob defends" with
    | Error [ e ] -> Assert.Contains("Expected 'then'", e.Message)
    | other -> Assert.Fail(sprintf "expected single error, got %A" other)

[<Fact>]
let ``parseCondition rejects unknown stat field`` () =
    let result = parseConditionString "Bob.Mana < 30"
    match result with
    | Error e -> Assert.Contains("HP, Attack, or Defense", e.Message)
    | Ok _ -> Assert.Fail("expected error for unknown stat field")

[<Fact>]
let ``parseStatements ignores line comments`` () =
    let script = """
# opening comment
Alice attacks Bob   # trailing comment after a turn
# blank-ish line below

Bob defends
"""
    let result = parseStatements script
    let expected =
        [
            SAction { Actor = "Alice"; Action = Attack (NamedTarget "Bob") }
            SAction { Actor = "Bob"; Action = Defend }
        ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements accepts quoted multi-word names`` () =
    let script = "\"Cave Troll\" attacks \"Sir Reginald\""
    let result = parseStatements script
    let expected =
        [ SAction { Actor = "Cave Troll"; Action = Attack (NamedTarget "Sir Reginald") } ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements accepts quoted name in condition`` () =
    let script = "if \"Cave Troll\".HP < 30 then \"Cave Troll\" defends"
    let result = parseStatements script
    let expected =
        [
            SIf(
                Compare(EStatRef("Cave Troll", StatField.HP), Lt, EIntLit 30),
                SAction { Actor = "Cave Troll"; Action = Defend },
                None)
        ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements parses attacks weakest with no group`` () =
    let result = parseStatements "Alice attacks weakest"
    let expected =
        [ SAction { Actor = "Alice"; Action = Attack (TargetSelector(Weakest, AnyGroup)) } ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements parses attacks weakest enemy`` () =
    let result = parseStatements "Alice attacks weakest enemy"
    let expected =
        [ SAction { Actor = "Alice"; Action = Attack (TargetSelector(Weakest, EnemyGroup)) } ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements parses attacks strongest ally`` () =
    let result = parseStatements "Bob attacks strongest ally"
    let expected =
        [ SAction { Actor = "Bob"; Action = Attack (TargetSelector(Strongest, AllyGroup)) } ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements parses casts on lowest ally (lowest is alias for Weakest)`` () =
    let result = parseStatements "Cleric casts Heal on lowest ally"
    let expected =
        [ SAction { Actor = "Cleric"; Action = CastSpell("Heal", TargetSelector(Weakest, AllyGroup)) } ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements parses attacks random`` () =
    let result = parseStatements "Alice attacks random"
    let expected =
        [ SAction { Actor = "Alice"; Action = Attack (TargetSelector(Random, AnyGroup)) } ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements parses attacks random enemy`` () =
    let result = parseStatements "Alice attacks random enemy"
    let expected =
        [ SAction { Actor = "Alice"; Action = Attack (TargetSelector(Random, EnemyGroup)) } ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements parses highest as alias for Strongest`` () =
    let result = parseStatements "Alice attacks highest any"
    let expected =
        [ SAction { Actor = "Alice"; Action = Attack (TargetSelector(Strongest, AnyGroup)) } ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

// ── Team blocks ──────────────────────────────────────────────────────────────

[<Fact>]
let ``parseStatements parses team declaration with semicolon-separated members`` () =
    let result = parseStatements "team Heroes { Alice; Bob; Cleric }"
    let expected = [ STeamDecl("Heroes", ["Alice"; "Bob"; "Cleric"]) ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements parses team declaration with newline-separated members`` () =
    let result = parseStatements "team Villains {\nDragon\nOrc\n}"
    let expected = [ STeamDecl("Villains", ["Dragon"; "Orc"]) ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements parses team declaration with single member`` () =
    let result = parseStatements "team Solo { Alice }"
    let expected = [ STeamDecl("Solo", ["Alice"]) ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

// ── Repeat blocks ─────────────────────────────────────────────────────────────

[<Fact>]
let ``parseStatements parses repeat block with single body statement`` () =
    let result = parseStatements "repeat 3 {\nAlice attacks Bob\n}"
    let expected =
        [ SRepeat(3, [ SAction { Actor = "Alice"; Action = Attack(NamedTarget "Bob") } ]) ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements parses repeat block with multiple body statements`` () =
    let result = parseStatements "repeat 2 {\nAlice attacks Bob\nBob defends\n}"
    let expected =
        [ SRepeat(2, [
            SAction { Actor = "Alice"; Action = Attack(NamedTarget "Bob") }
            SAction { Actor = "Bob";   Action = Defend }
          ]) ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements parses repeat 0`` () =
    let result = parseStatements "repeat 0 {\nAlice attacks Bob\n}"
    let expected = [ SRepeat(0, [ SAction { Actor = "Alice"; Action = Attack(NamedTarget "Bob") } ]) ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

// ── Named group targeting ─────────────────────────────────────────────────────

[<Fact>]
let ``parseStatements parses attacks weakest named group`` () =
    let result = parseStatements "Alice attacks weakest Heroes"
    let expected = [ SAction { Actor = "Alice"; Action = Attack(TargetSelector(Weakest, NamedGroup "Heroes")) } ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements parses attacks random named group`` () =
    let result = parseStatements "Alice attacks random Villains"
    let expected = [ SAction { Actor = "Alice"; Action = Attack(TargetSelector(Random, NamedGroup "Villains")) } ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements parses casts on strongest named group`` () =
    let result = parseStatements "Cleric casts Heal on strongest Heroes"
    let expected = [ SAction { Actor = "Cleric"; Action = CastSpell("Heal", TargetSelector(Strongest, NamedGroup "Heroes")) } ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements reports error when repeat has no integer`` () =
    let result = parseStatements "repeat Alice attacks Bob"
    match result with
    | Error errors -> Assert.Contains("Expected integer count after 'repeat'", errors |> List.map (fun e -> e.Message) |> String.concat " ")
    | Ok _ -> Assert.Fail "Expected parse error"
