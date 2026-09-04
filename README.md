# Sit and Do Stuff

A SMAPI mod for Stardew Valley. Compatible with SV 1.6.15+.

**Don't just sit there; do stuff! This mod lets you perform actions while sitting, such as watching TV,
talking to NPCs, petting animals, and more.**

Configurable via **Generic Mod Config Menu (GMCM)** (optional, but strongly recommended).

## How it works in-game

1. Sit down as normal. Nothing is targeted initially.
2. Cycle through targetable objects with `A`/`D` (controller: left/right on `D-Pad`).
   1. Cycling goes left to right relative to the way you're facing, wrapping back around to
      nothing targeted: `None -> Target 1 -> ... -> Target N -> None`.
   2. The highlight can be a yellow square around the object's tile, a small animated arrow above
      it, or both, plus the object's name.
3. **If targeting an object:** the interact button (`X`/`right-click`) (controller: `A`) interacts with it.
4. **If not targeting anything:** the interact button will stand you up as normal.
5. `C`/`left-click` (controller: `B`) always stands you up immediately, canceling any targeting.

### Available actions

- Use furniture items (placed furniture, not the same as fixed map furniture in some buildings):
  - TV
  - Telephone
  - Workbench
  - Farm Computer
  - Arcade Systems
  - Sewing Machine
  - Mini-Jukebox
- Talk to NPCs
- Gift NPCs (off by default - see settings)
- Pet pets/farm animals
- Order at the Saloon bar

### Additional info

Only objects you're facing (directly ahead, diagonally ahead, or straight to either side) can be
targeted, except the Saloon bar, which works from any direction. Turn this off with "Require
Facing Target" in settings to allow targeting in any direction.

Each type of action can be individually configured. The options for each type are:

- **Enabled:** turns targeting/interacting with that category on or off.
- **Range:** how far away it can be, from 1 tile (only the 8 tiles touching you) up to 10.

I tried allowing Fishing and Eating while sitting but these proved to be very complicated and bug prone; and so I was 
unable to get it to work. I do not believe it is possible due to how the game code works.

Without GMCM installed, settings can be hand-edited in `config.json` after the mod runs once.

## Project layout

```
SitAndDoStuff/
  manifest.json              SMAPI mod manifest
  SitAndDoStuff.csproj        Project file (uses Pathoschild.Stardew.ModBuildConfig)
  ModEntry.cs                 Mod entry point: input handling, session lifecycle
  ModConfig.cs                Config schema (keybinds + per-category enable/range)
  GMCMIntegration.cs          Generic Mod Config Menu hookup (local API interface + menu builder)
  Targeting/
    InteractionCategory.cs    Enum of everything the mod can target
    InteractionTarget.cs      A single candidate: where it is, its label, how to trigger it
    TargetFinder.cs           Scans the current location for valid, in-range, facing targets
    TargetSession.cs          Tracks the active target list + current selection while cycling
    TargetRenderer.cs         Draws the highlight
  i18n/default.json           Translation stub
```

## How to build it

You'll need the game installed locally (Steam/GOG) plus SMAPI. This project uses
`Pathoschild.Stardew.ModBuildConfig`, which finds your game install automatically and copies the
build output straight into your `Mods` folder.

1. Install the .NET 6 SDK.
2. Open a terminal in this folder and run:
   ```
   dotnet build
   ```
3. If the build package can't auto-detect your game path, create a `SitAndDoStuff.csproj.user`
   file (or set the `GamePath` MSBuild property / `GAME_PATH` environment variable) pointing at
   your Stardew Valley install folder. See the package's docs:
   https://www.nuget.org/packages/Pathoschild.Stardew.ModBuildConfig
4. On success, the mod is copied into your `Mods/SitAndDoStuff` folder automatically. Launch the
   game through SMAPI.

## Extending it

- To add a new "nearby object" interaction type: add a case to `InteractionCategory`, a
  `CategorySettings` property in `ModConfig` (plus a case in its `Get()` switch), a settings-menu
  entry in `GMCMIntegration`'s `categories` list, and a scanning block in
  `TargetFinder.FindTargets`.
- To recognize an object under a different name (e.g. a mod-added reskin), adjust the string
  matches in `TargetFinder.ClassifyObject`.

## Disclaimer and credits

This is my first mod, and I'm new to C#, therefore I did use AI to create the initial mod code so
that I could have a jumpstart and a good foundation for how mods work. Further work after the
initial creation was hand-coded by me.

Developed by [BlueCaret](https://www.bluecaret.com/).

Check out my Stardew Valley companion app, [Gunther's Library](https://www.bluecaret.com/guntherslibrary/)

[Buy Me A Coffee](https://www.buymeacoffee.com/bluecaret)
