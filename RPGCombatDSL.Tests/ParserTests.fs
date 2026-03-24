module ParserTests

open Parser
open Types
open Xunit

[<Fact>]
let ``valid lines are parsed in order`` () =
    let script = "Alice attacks Bob\nBob defends"

    let turns = parseTurns script

    let expected =
        [
            { Actor = "Alice"; Action = Attack "Bob" }
            { Actor = "Bob"; Action = Defend }
        ]

    Assert.Equal<Turn list>(expected, turns)

[<Fact>]
let ``invalid lines are ignored`` () =
    let script = "Alice attacks Bob\nunknown command\nBob defends"

    let turns = parseTurns script

    let expected =
        [
            { Actor = "Alice"; Action = Attack "Bob" }
            { Actor = "Bob"; Action = Defend }
        ]

    Assert.Equal<Turn list>(expected, turns)
