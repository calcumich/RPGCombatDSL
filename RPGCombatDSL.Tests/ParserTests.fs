module ParserTests

open Parser
open Types
open Xunit

[<Fact>]
let ``parseTurns parses supported commands in order`` () =
    let script = """
Alice attacks Bob
Bob defends
"""

    let turns = parseTurns script

    let expected =
        [
            { Actor = "Alice"; Action = Attack "Bob" }
            { Actor = "Bob"; Action = Defend }
        ]

    Assert.Equal<Result<Turn list, ParseError list>>(Ok expected, turns)

[<Fact>]
let ``parseTurns parses uses and casts actions`` () =
    let script = """
Alice uses HealthPotion
Bob casts Fireball on Alice
Cleric casts Heal
"""

    let turns = parseTurns script

    let expected =
        [
            { Actor = "Alice"; Action = UseItem "HealthPotion" }
            { Actor = "Bob"; Action = CastSpell("Fireball", "Alice") }
            { Actor = "Cleric"; Action = CastSpell("Heal", "") }
        ]

    Assert.Equal<Result<Turn list, ParseError list>>(Ok expected, turns)

[<Fact>]
let ``parseTurns returns an error list when an unknown action is used`` () =
    let script = """
Alice attacks Bob
Alice frobnicates Bob
"""

    let result = parseTurns script

    let expected =
        [
            {
                LineNumber = 3
                LineText = "Alice frobnicates Bob"
                Message = "Expected an action (attacks, defends, uses, casts)"
            }
        ]

    Assert.Equal<Result<Turn list, ParseError list>>(Error expected, result)

[<Fact>]
let ``parseScript returns both valid turns and invalid lines`` () =
    let script = """
Alice attacks Bob
Alice frobnicates Bob
Bob defends
"""

    let result = parseScript script

    let expectedTurns =
        [
            { Actor = "Alice"; Action = Attack "Bob" }
            { Actor = "Bob"; Action = Defend }
        ]

    Assert.Equal<Turn list>(expectedTurns, result.Turns)
    Assert.Equal(1, result.Errors.Length)
    Assert.Equal(3, result.Errors.[0].LineNumber)
    Assert.Equal("Alice frobnicates Bob", result.Errors.[0].LineText)

[<Fact>]
let ``parseTurns reports missing target after attacks`` () =
    let script = "Alice attacks"

    let result = parseTurns script

    match result with
    | Error [ e ] ->
        Assert.Equal(1, e.LineNumber)
        Assert.Contains("target name after 'attacks'", e.Message)
    | other ->
        Assert.Fail(sprintf "Expected single error, got %A" other)
