module NarratorTests

open Types
open Narrator
open Xunit

[<Fact>]
let ``render RoundStarted`` () =
    Assert.Equal("--- Round 2 ---", render [ RoundStarted 2 ])

[<Fact>]
let ``render TurnTaken is suppressed`` () =
    Assert.Equal("", render [ TurnTaken("Alice", Defend) ])

[<Fact>]
let ``render DamageDealt`` () =
    Assert.Equal("Alice strikes Bob for 15 damage.", render [ DamageDealt("Alice", "Bob", 15) ])

[<Fact>]
let ``render HealApplied`` () =
    Assert.Equal("Cleric restores 15 HP to Alice.", render [ HealApplied("Cleric", "Alice", 15) ])

[<Fact>]
let ``render StatBoosted Defense`` () =
    Assert.Equal("Alice gains +5 Defense.", render [ StatBoosted("Alice", StatField.Defense, 5) ])

[<Fact>]
let ``render StatBoosted Attack`` () =
    Assert.Equal("Alice gains +5 Attack.", render [ StatBoosted("Alice", StatField.Attack, 5) ])

[<Fact>]
let ``render TargetMissed`` () =
    Assert.Equal(
        "Alice finds no target (no eligible target).",
        render [ TargetMissed("Alice", "no eligible target") ])

[<Fact>]
let ``render CharDefeated`` () =
    Assert.Equal("Bob has been defeated!", render [ CharDefeated "Bob" ])

[<Fact>]
let ``render BattleEnded Winner`` () =
    Assert.Equal("heroes wins after 3 rounds.", render [ BattleEnded(Winner "heroes", 3) ])

[<Fact>]
let ``render BattleEnded Draw`` () =
    Assert.Equal("Draw after 5 rounds.", render [ BattleEnded(Draw, 5) ])

[<Fact>]
let ``render multiple events joined by newline, TurnTaken suppressed`` () =
    let events =
        [ RoundStarted 1
          TurnTaken("Alice", Attack(NamedTarget "Bob"))
          DamageDealt("Alice", "Bob", 15)
          CharDefeated "Bob"
          BattleEnded(Winner "heroes", 1) ]
    let expected =
        "--- Round 1 ---\nAlice strikes Bob for 15 damage.\nBob has been defeated!\nheroes wins after 1 rounds."
    Assert.Equal(expected, render events)
