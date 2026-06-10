module Types

type Stat = { HP: int; Attack: int; Defense: int }

type Character = {
    Name: string
    Stats: Stat
    Side: string
}

type Modifier =
    | Weakest
    | Strongest
    | Random

type Group =
    | EnemyGroup
    | AllyGroup
    | AnyGroup
    | NamedGroup of string

type TargetSpec =
    | NamedTarget of string
    | TargetSelector of Modifier * Group

type Action =
    | Attack of TargetSpec
    | Defend
    | UseItem of string
    | CastSpell of string * TargetSpec

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

/// An expression evaluated against the current world state. Numeric only;
/// targeting selectors (weakest enemy, lowest ally, etc.) use TargetSpec instead.
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
    | SAction  of Turn
    | SIf      of Condition * thenBranch: Statement * elseBranch: Statement option
    | STeamDecl of teamName: string * members: string list
    | SRepeat  of count: int * body: Statement list
