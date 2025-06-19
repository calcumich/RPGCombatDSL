module ParserTests

open Xunit
open Parser
open Types

[<Fact>]
let ``valid lines are parsed`` () =
    let script = "Alice attacks Bob\nBob defends"
    let turns = parseTurns script
    Assert.Equal(2, List.length turns)
    let first = List.head turns
    Assert.Equal("Alice", first.Actor)
    match first.Action with
    | Attack "Bob" -> ()
    | _ -> failwith "unexpected action"

[<Fact>]
let ``invalid lines are ignored`` () =
    let script = "Alice attacks Bob\nunknown command\nBob defends"
    let turns = parseTurns script
    Assert.Equal(2, List.length turns)
    match List.last turns with
    | { Actor = "Bob"; Action = Defend } -> ()
    | _ -> failwith "unexpected last turn"

