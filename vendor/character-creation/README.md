# Character Creation Rules & Static Web Application

Vendored copy of U8_Ash character creation rules, engine source, and a standalone JavaScript migration for static web deployment.

## Contents

- `character-creation.json`: JSON configuration specifying ability score curves, playability thresholds, dice count per score, and class priority matrices.
- `csharp/`: Direct copies of the C# rules engine implementations (`CharacterCreation.cs`, `CharacterCreationData.cs`, `AbilityScores.cs`, `Dice.cs`).
- `webapp/`: Standalone static web application implementing **Ironman** and **Unearthed Arcana** character generation methods in pure Vanilla JS, HTML, and CSS.

## Generation Methods

### 1. Ironman Method
- **Roll First, Choose Second**: Rolls 3d6 down the score order (`STR`, `DEX`, `CON`, `INT`, `WIS`, `CHA`).
- **Playability Filter**: Keeps a rolled score set only if:
  - $\ge$ 2 scores are 15 or higher.
  - $\le$ 1 score is lower than 6.
  - Automatically rerolls until valid, recording total attempt count.
- **Class Choice**: After scores are known, player chooses class (`Fighter`, `Rogue`, `Cleric`, `Wizard`).

### 2. Unearthed Arcana Method
- **Choose First, Roll Pools**: Player chooses class first.
- **Class Priorities**: Class priority assigns dice pools (`8d6`, `7d6`, `6d6`, `5d6`, `4d6`, `3d6`) in rank order.
- **Keep Highest 3**: Each pool keeps its 3 highest dice to produce stat scores.
  - **Fighter**: STR > DEX > CON > CHA > WIS > INT
  - **Rogue**: DEX > INT > CHA > CON > STR > WIS
  - **Cleric**: WIS > CHA > STR > INT > CON > DEX
  - **Wizard**: INT > WIS > DEX > CON > CHA > STR

## Running the Web App

Open `webapp/index.html` directly in any web browser. No server, node build, or external dependencies required.
