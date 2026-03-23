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
let ``parseTurns returns an error list when unsupported lines are present`` () =
    let script = """
Alice attacks Bob
Alice uses HealthPotion
"""

    let result = parseTurns script

    let expected =
        [
            {
                LineNumber = 2
                LineText = "Alice uses HealthPotion"
                Message = "Unsupported command"
            }
        ]

    Assert.Equal<Result<Turn list, ParseError list>>(Error expected, result)

[<Fact>]
let ``parseScript returns both valid turns and invalid lines`` () =
    let script = """
Alice attacks Bob
Alice uses HealthPotion
Bob defends
"""

    let result = parseScript script

    let expectedTurns =
        [
            { Actor = "Alice"; Action = Attack "Bob" }
            { Actor = "Bob"; Action = Defend }
        ]

    let expectedErrors =
        [
            {
                LineNumber = 2
                LineText = "Alice uses HealthPotion"
                Message = "Unsupported command"
            }
        ]

    Assert.Equal<Turn list>(expectedTurns, result.Turns)
    Assert.Equal<ParseError list>(expectedErrors, result.Errors)
