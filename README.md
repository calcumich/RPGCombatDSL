# RPGCombatDSL

This repository contains a small F# project that demonstrates a domain specific language (DSL) for describing simple turn based RPG combat.

The DSL lets you express combat turns in a very compact form and the program simulates those turns, printing the resulting hit points for each character.

## Quick start

1. Install the [.NET SDK](https://dotnet.microsoft.com/download) (version 8.0 or later).
2. From the repository root run:

   ```bash
   dotnet run --project RPGCombatDSL/RPGCombatDSL.fsproj
   ```

   The bundled `Program.fs` contains a short script that is parsed and executed when you run the project.

## Example DSL script

The default script included with the project looks like this:

```text
Alice attacks Bob
Bob defends
```

Running the program processes these turns and prints the final hit points for each character:

```
Alice - HP: 100
Bob - HP: 75
```

The script parser is intentionally minimal but can be extended to support additional actions. Check the `Parser.fs`, `Engine.fs` and `Program.fs` files for the implementation details.

