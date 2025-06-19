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