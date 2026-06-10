# DSL syntax walkthrough

A tour of what you can write in a `.combat` script today. This is a **language reference**, not a code walkthrough — for how the parser/engine work, see [Lexer.md](./Lexer.md), [Parser.md](./Parser.md), [Types.md](./Types.md), and [Engine.md](./Engine.md).

## The shape of a script

A script is a sequence of **statements**, one per line (or one block per `{ ... }`). Blank lines and lines that contain only whitespace or a comment are skipped.

```
# Declare teams, then run a fight
team Heroes { Alice; Cleric }
team Villains { Goblin }
repeat 3 { Alice attacks weakest Villains }
if Goblin.HP < 30 then Cleric casts Heal on weakest Heroes
```

Four kinds of statement:

1. A **team declaration** assigns characters to a named group.
2. A **repeat block** runs its body N times.
3. An **action statement** names an actor and what they do.
4. A **conditional statement** runs an inner action only if a condition holds.

Everything below is a more detailed look at each piece.

## Comments

Anywhere a `#` appears, the rest of the line is a comment.

```
# A whole-line comment
Alice attacks Bob   # trailing comment on the same line
```

Comments are stripped by the lexer, so the parser never sees them. The newline at the end of a comment line is still significant — it terminates whatever statement was on that line (if any).

## Identifiers and quoted names

A bare **identifier** is a run of letters, digits, and underscores starting with a letter or underscore: `Alice`, `Bob_42`, `HealthPotion`, `Fireball`.

If a name has spaces or other punctuation, wrap it in **double quotes**:

```
"Cave Troll" attacks "Sir Reginald"
"Sir Reginald" casts Heal on "Cave Troll"
```

Quoted strings work anywhere an identifier does — actor names, target names, spell names, item names, and the character side of a stat reference (`"Cave Troll".HP`).

These **keywords** are reserved and can't be used as bare identifiers: `attacks`, `defends`, `uses`, `casts`, `on`, `if`, `then`, `else`, `team`, `repeat`. If you need a character literally named one of these, quote it: `"repeat"`.

## Action statements

Every action statement has the shape `<actor> <action>`, where `<actor>` is a name and `<action>` is one of four forms.

### attacks

```
Alice attacks Bob
Alice attacks weakest enemy
Alice attacks random Villains
```

The actor strikes one target. Damage is `max 1 (actor.Attack − target.Defense)`. The minimum-of-one rule means there's no such thing as a fully-shrugged-off attack.

The target can be a literal name or a **targeting selector** (see the next section). If the target doesn't exist or no candidate matches the selector, the line parses fine but the engine leaves state unchanged.

### defends

```
Bob defends
```

The actor's `Defense` stat goes up by 5. There's no concept of "defending until next turn" — it's a permanent stat bump. (That's not necessarily realistic, but it's what the engine does today.)

### uses

```
Alice uses HealthPotion
```

The actor consumes an item. The engine recognizes three item names (case-insensitively):

| Item            | Effect                       |
|-----------------|------------------------------|
| `HealthPotion`  | actor's HP +20               |
| `PowerPotion`   | actor's Attack +5            |
| `DefensePotion` | actor's Defense +5           |

Any other item name parses but is a no-op at runtime.

### casts

```
Bob casts Fireball on Alice
Cleric casts Heal on weakest allies
Cleric casts Heal           # no target → no-op
```

The actor casts a spell, optionally on a target. The engine recognizes two spell names (case-insensitively):

| Spell      | Effect                                                  |
|------------|---------------------------------------------------------|
| `Fireball` | damages target: `max 1 (30 − target.Defense)` HP loss   |
| `Heal`     | restores target HP by 15                                |

`on <target>` accepts a literal name or a targeting selector. If `on` is omitted entirely, the target is the empty string and the engine treats the spell as a no-op.

## Targeting selectors

Wherever a target name appears after `attacks` or `on`, you can use a **selector** instead of a literal name. A selector picks one character from a filtered pool at runtime.

```
<modifier> <group>
```

**Modifiers** — how to pick from the pool:

| Modifier | Aliases | Picks |
|---|---|---|
| `weakest` | `lowest` | character with the lowest current HP |
| `strongest` | `highest` | character with the highest current HP |
| `random` | — | a random character from the pool |

**Groups** — which characters to consider:

| Group | Meaning |
|---|---|
| `enemy` / `enemies` | characters whose `Side` differs from the actor's |
| `ally` / `allies` | characters whose `Side` matches the actor's |
| `any` | all characters |
| any other word | characters whose `Side` equals that exact word (a named team) |

Examples:

```
Alice attacks weakest enemy       # lowest-HP foe
Cleric casts Heal on lowest ally  # lowest-HP teammate
Boss attacks random any           # random character, either side
Alice attacks strongest Villains  # strongest member of the Villains team
```

If the selector matches no candidates (e.g. no enemies remain, or no one has that Side), the action is a no-op.

## Team declarations

```
team <name> { <member>; <member>; ... }
```

Assigns the named characters to a team by setting their `Side` field. Once a character's `Side` is set, targeting selectors that reference `enemy`, `ally`, or a specific team name resolve against it.

```
team Heroes { Alice; Cleric }
team Villains { Goblin; Orc }
```

Members are separated by `;` or newlines:

```
team Villains {
    Goblin
    Orc
}
```

**Team declarations must appear before any action that depends on them.** The engine processes statements top-to-bottom; a selector like `attacks weakest Villains` reads the current `Side` values at the moment it runs.

If a member name doesn't match any character in the current battle, that name is silently skipped.

## Repeat blocks

```
repeat <n> { <statements> }
```

Runs the block body `n` times in sequence. Each iteration sees the state produced by the previous one — HP changes, defense boosts, and side assignments all persist across iterations.

```
repeat 3 { Alice attacks Bob }
```

Multi-statement bodies use newlines inside the braces:

```
repeat 5 {
    Alice attacks weakest Villains
    Bob defends
}
```

`repeat 0` is a no-op. The block can contain any statement type, including conditionals:

```
repeat 10 {
    Alice attacks weakest enemy
    if Bob.HP < 30 then Bob uses HealthPotion
}
```

## Conditional statements

```
if <condition> then <statement>
if <condition> then <statement> else <statement>
```

A conditional evaluates `<condition>` against the **current** character state, then runs either the `then` statement or the `else` statement (if present). If the condition is false and there's no `else`, nothing happens — state is unchanged.

Conditionals nest by themselves: the `then` branch and the `else` branch are each a full statement, which can itself be another `if`.

```
if Bob.HP < 30 then if Bob.Defense < 10 then Bob defends
if Alice.HP > 50 then Alice attacks Bob else Alice defends
```

The `then` and `else` branches are single statements — not `{ ... }` blocks. To run multiple statements conditionally, use a repeat block containing the conditional.

### Conditions

A condition is two expressions joined by a comparison operator:

```
<expr> <comparator> <expr>
```

Comparators: `<`, `<=`, `>`, `>=`, `==`, `!=`. Both sides must be integers.

There are no boolean operators yet (`and`, `or`, `not`) — that's a planned addition. One comparison per `if`.

### Expressions

An expression is either an integer literal or a **stat reference**:

```
30                    # integer literal
Bob.HP                # Bob's current HP
"Cave Troll".Defense  # quoted character + stat
```

Only three stat fields are addressable: `HP`, `Attack`, `Defense`. Anything else after the `.` is a parse error:

```
if Bob.Mana < 30 then Bob defends
# → parse error: Expected HP, Attack, or Defense after '.'
```

Stat references resolve against state **at the moment the condition is evaluated**, not at parse time. So in a long script, `Bob.HP < 30` checks Bob's HP right now — after every preceding turn has run.

## Errors and recovery

When the parser hits a malformed line, it reports the error (with line number and the offending text) and skips to the next newline. The rest of the script still parses, so one bad line doesn't take down the whole batch.

```
Alice attacks Bob
Alice frobnicates Bob       # ← parse error here
Bob defends                 # still parses
```

Running this returns one error and two valid statements.

## Putting it all together

A script using every feature:

```
# Declare sides.
team Heroes   { Alice; Cleric }
team Villains { Goblin }

# Opening volley — three rounds of Alice hitting the weakest foe.
repeat 3 { Alice attacks weakest Villains }

# Goblin counter-attacks; Cleric heals if the situation looks bad.
Goblin attacks weakest Heroes
if Alice.HP < 50 then Cleric casts Heal on weakest Heroes else Alice defends

# Multi-word names work too.
"Cave Troll" attacks Alice
if "Cave Troll".HP > 50 then Alice casts Fireball on "Cave Troll" else Alice defends
```

## What's not in the language (yet)

These are deliberate omissions, not bugs — see [docs/ideas.md](../../docs/ideas.md) for the roadmap.

- **Boolean combinators** (`and`, `or`, `not`) in conditions — one comparison per `if` today.
- **Block form for `then`/`else`** — the branches of a conditional are a single statement, not a `{ ... }` block.
- **Arithmetic in expressions** — only literals and stat refs; `Bob.HP + 5` is not valid.
- **Stats beyond HP / Attack / Defense.**

The recursive-descent parser was built so adding new statement forms or expression variants is mostly a matter of a new token plus a new branch.
