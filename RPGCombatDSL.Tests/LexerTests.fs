module LexerTests

open Lexer
open Xunit

let private tokensOf source =
    tokenize source |> List.map (fun pt -> pt.Token)

[<Fact>]
let ``tokenizes integer literals`` () =
    let toks = tokensOf "30"
    Assert.Equal<Token list>([ TInt 30; TEof ], toks)

[<Fact>]
let ``tokenizes dotted stat reference`` () =
    let toks = tokensOf "Bob.HP"
    Assert.Equal<Token list>(
        [ TIdent "Bob"; TDot; TIdent "HP"; TEof ],
        toks)

[<Fact>]
let ``tokenizes comparison operators`` () =
    let toks = tokensOf "< <= > >= == !="
    Assert.Equal<Token list>(
        [ TLt; TLe; TGt; TGe; TEqEq; TNeq; TEof ],
        toks)

[<Fact>]
let ``tokenizes if then else keywords`` () =
    let toks = tokensOf "if then else"
    Assert.Equal<Token list>([ TIf; TThen; TElse; TEof ], toks)

[<Fact>]
let ``skips line comments but keeps trailing newline`` () =
    let toks = tokensOf "Alice # this is a comment\nBob"
    Assert.Equal<Token list>(
        [ TIdent "Alice"; TNewline; TIdent "Bob"; TEof ],
        toks)

[<Fact>]
let ``tokenizes quoted string as TString`` () =
    let toks = tokensOf "\"Cave Troll\""
    Assert.Equal<Token list>([ TString "Cave Troll"; TEof ], toks)
