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

    Assert.Equal<Turn list>(expected, turns)

[<Fact>]
let ``parseTurns skips unsupported lines`` () =
    let script = """
Alice attacks Bob
Alice uses HealthPotion
"""

    let turns = parseTurns script

    let expected = [ { Actor = "Alice"; Action = Attack "Bob" } ]

    Assert.Equal<Turn list>(expected, turns)
