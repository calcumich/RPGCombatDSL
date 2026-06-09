module Types

type Stat = { HP: int; Attack: int; Defense: int }

type Character = {
    Name: string
    Stats: Stat
}

type Action =
    | Attack of string
    | Defend
    | UseItem of string
    | CastSpell of string * string

type Turn = {
    Actor: string
    Action: Action
}

/// A field on a character's Stat record, addressable from the DSL as
/// `Character.HP`, `Character.Attack`, `Character.Defense`.
[<RequireQualifiedAccess>]
type StatField =
    | HP
    | Attack
    | Defense

/// An expression evaluated against the current world state. Numeric for now;
/// will grow to cover targeting modifiers like `weakest enemy`.
type Expr =
    | EIntLit of int
    | EStatRef of character: string * field: StatField

type Comparator =
    | Lt
    | Le
    | Gt
    | Ge
    | Eq
    | Ne

/// A boolean test used as the head of an `if` statement.
type Condition =
    | Compare of Expr * Comparator * Expr

/// A top-level script item.
type Statement =
    | SAction of Turn
    | SIf of Condition * thenBranch: Statement * elseBranch: Statement option
