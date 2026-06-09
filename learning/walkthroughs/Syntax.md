# DSL syntax walkthrough

A tour of what you can write in a `.combat` script today. This is a **language reference**, not a code walkthrough — for how the parser/engine work, see [Lexer.md](./Lexer.md), [Parser.md](./Parser.md), [Types.md](./Types.md), and [Engine.md](./Engine.md).

## The shape of a script

A script is a sequence of **statements**, one per line. Blank lines and lines that contain only whitespace or a comment are skipped.

```
# A small fight
Alice attacks Bob
Bob defends
if Bob.HP < 80 then Bob uses HealthPotion
```

Three things are happening:

1. A line comment (`# ...`) is ignored.
2. Two **action statements** name an actor and what they do.
3. One **conditional statement** runs an inner action only if a condition holds.

That's the whole language. Everything below is a more detailed look at each piece.

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

The five **keywords** are reserved and can't be used as bare identifiers: `attacks`, `defends`, `uses`, `casts`, `on`, `if`, `then`, `else`. If you need a character literally named `If`, quote it: `"If"`.

## Action statements

Every action statement has the shape `<actor> <action>`, where `<actor>` is a name and `<action>` is one of four forms.

### attacks

```
Alice attacks Bob
```

The actor strikes the target. Damage is `max 1 (actor.Attack − target.Defense)`. The minimum-of-one rule means there's no such thing as a fully-shrugged-off attack.

If the target name doesn't exist in the character map, the line parses fine but the engine leaves state unchanged.

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
Cleric casts Heal           # no target → empty string target
```

The actor casts a spell, optionally on a target. The engine recognizes two spell names (case-insensitively):

| Spell      | Effect                                                  |
|------------|---------------------------------------------------------|
| `Fireball` | damages target: `max 1 (30 − target.Defense)` HP loss   |
| `Heal`     | restores target HP by 15                                |

`on <target>` is optional in the grammar. If it's omitted, the target is the empty string and the engine treats the spell as a no-op (since "" isn't in the character map).

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

The whole statement still has to fit on one line — there are no `{ ... }` blocks yet.

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

A script using every form:

```
# Opening exchange.
Alice attacks Bob
Bob defends

# Bob heals if he's hurt.
if Bob.HP < 80 then Bob uses HealthPotion

# Multi-word names work too.
"Cave Troll" attacks Alice
if "Cave Troll".HP > 50 then Alice casts Fireball on "Cave Troll" else Alice defends
```

## What's not in the language (yet)

These are deliberate omissions, not bugs — see [docs/ideas.md](../../docs/ideas.md) for the roadmap.

- **Boolean combinators** (`and`, `or`, `not`) in conditions.
- **Block form** for `then`/`else` (`{ stmt; stmt; ... }`).
- **Loops or repeats** (`repeat 3 { ... }`).
- **Targeting modifiers** (`attacks weakest enemy`, `casts Heal on lowest ally`).
- **Team / party declarations** (`team Heroes { Alice; Bob }`).
- **Arithmetic in expressions** (only literals and stat refs — no `Bob.HP + 5`).
- **Stats beyond HP / Attack / Defense.**

Each of these slots into the existing grammar without rework — the recursive-descent parser was built so adding new statement forms or expression variants is mostly a matter of a new token plus a new branch.
