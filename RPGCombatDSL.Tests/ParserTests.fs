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
            SAction { Actor = "Alice"; Action = Attack "Bob" }
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
            SAction { Actor = "Bob"; Action = CastSpell("Fireball", "Alice") }
            SAction { Actor = "Cleric"; Action = CastSpell("Heal", "") }
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
            SAction { Actor = "Alice"; Action = Attack "Bob" }
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
                SAction { Actor = "Alice"; Action = Attack "Bob" },
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
            SAction { Actor = "Alice"; Action = Attack "Bob" }
            SAction { Actor = "Bob"; Action = Defend }
        ]
    Assert.Equal<Result<Statement list, ParseError list>>(Ok expected, result)

[<Fact>]
let ``parseStatements accepts quoted multi-word names`` () =
    let script = "\"Cave Troll\" attacks \"Sir Reginald\""
    let result = parseStatements script
    let expected =
        [ SAction { Actor = "Cave Troll"; Action = Attack "Sir Reginald" } ]
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
