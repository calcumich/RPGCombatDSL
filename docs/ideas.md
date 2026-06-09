# Project ideas

Directions this project could grow in. Rough ordering: from "natural next step" to "actually fun."

## 1. Close the parser/engine gap (done)

`Engine.fs` handled `UseItem` and `CastSpell` (health potions, fireball, heal, etc.) but `Parser.fs` only knew `attacks` and `defends`. Adding `Alice uses HealthPotion` and `Cleric casts Heal on Bob` unlocked features that were already implemented.

**Status:** done as part of the lexer/parser rewrite — both actions are now reachable from scripts.

## 2. Turn it into a real DSL

The grammar is still tiny: one statement per line, no expressions, no control flow. The recursive-descent parser is in place, so adding features is mostly mechanical from here:

- **Conditionals:** `if Bob.HP < 30 then Bob uses HealthPotion`. Needs `TIf`/`TThen`/`TElse` tokens, a `parseStatement` that dispatches between turn and conditional, and a small expression parser (Pratt-style precedence climbing is the standard pattern for comparison + boolean operators).
- **Targeting modifiers:** `Alice attacks weakest enemy`, `Cleric casts Heal on lowest ally`. Resolves at engine time using current state.
- **Multi-word or quoted names:** `"Cave Troll" attacks "Sir Reginald"`. One new lexer branch for a string-literal token.
- **Comments:** `# this is a comment` to end of line. One new lexer branch.
- **Party / team blocks:** `team Heroes { Alice; Bob; Cleric }` so targeting modifiers (`attacks any Heroes`) and win conditions have something to refer to.
- **Macros / repeats:** `repeat 3 { Alice attacks Bob }`.

**Why it's worth doing:** every other idea on this page gets easier once the grammar can describe more than a flat list of turns.

## 3. Battle scripts as AI for an actual game loop

Instead of running one canned script start-to-finish, let each character have a **behavior script** that picks their next action based on the current world state. Then run a real combat loop — initiative, turn order, win condition — with each character's script consulted on their turn.

Once you have that:
- Two scripts can fight each other. The DSL is now describing **strategy**, not a fixed sequence.
- Tournaments. Round-robin a folder of scripts, log results, see whose strategy wins.
- Iteration: tweak a script, re-run the tournament, watch the ranking move.

Pairs naturally with conditionals from idea #2 — most useful strategy scripts will be `if/then` rules over current state.

## 4. Replay / trace output

The engine currently mutates a map and prints final HP. Instead, emit a **structured log** of every turn: actor, action, dice rolls, damage computed, HP delta, resulting state. Then have two renderers:

- **Narrative**: "Alice swings at Bob for 15 damage (20 attack − 5 defense). Bob staggers; HP 75."
- **JSON / structured**: feed it to a frontend, replay viewer, or analytics.

This pairs really well with **property-based testing** via FsCheck: generate random scripts, run them, assert invariants ("HP never goes above max", "damage is always ≥ 1", "a defeated character takes no further turns"). Property-based testing tends to find weird engine bugs that example-based tests miss.

## 5. Language-server-ish tooling

The parser already returns structured errors with line numbers (`ParseError` with `LineNumber`, `LineText`, `Message`). Two cheap, disproportionately cool builds on top of that:

- **`--check` mode:** `dotnet run -- --check script.combat` parses without running and prints errors. Hooks into CI for any script files in the repo.
- **Tiny VS Code extension:** a Language Server Protocol shim that calls `--check` on save and reports diagnostics. Squiggles under bad lines. Maybe basic syntax highlighting via a TextMate grammar (no LSP needed for that part).

Once you've done the LSP shim, "go to definition" for spells/items defined in the script and "hover for stats" are both small extensions of the same machinery.

## Honest pick

If the goal is **the highest fun-to-effort ratio**, idea #2 (grow the grammar) followed by idea #3 (script-vs-script combat) is the path. Conditionals + `attacks weakest enemy` is the minimum viable interesting AI, and at that point the project stops being a syntax demo and starts being a thing you can play with.
