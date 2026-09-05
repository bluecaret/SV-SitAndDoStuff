# Sit and Do Stuff

A SMAPI mod for Stardew Valley. Compatible with SV 1.6.15+.

**Don't just sit there; do stuff! This mod lets you perform actions while sitting, such as watching TV,
talking to NPCs, petting animals, and more.**

Configurable via **Generic Mod Config Menu (GMCM)** (optional, but strongly recommended).

## How it works in-game

1. Sit down as normal. Nothing is targeted initially.
2. Cycle through targetable objects with the move left/right buttons.
   1. Cycling goes left to right relative to the way you're facing, wrapping back around to
      nothing targeted: `None -> Target 1 -> ... -> Target N -> None`.
   2. The highlight can be any combination of: a yellow square around the object's tile footprint, a small animated arrow above
      it, and the object's name above it.
3. Once targeting an object, use the action button like normal to interact.
4. To stand up while targeting, use the tool button.
5. With nothing targeted, either button just stands you up - unless you have food/a drink or a
   fishing rod selected and the matching experimental setting enabled, in which case the action
   button eats/drinks it, or the tool button charges/releases a fishing cast.

### Available actions while sitting

- Talk to NPCs
- Gift NPCs (off by default)
- Order at the Saloon bar
- Pet pets/farm animals
- Fishing (experimental - see below, off by default)
- Eat food (experimental - see below, off by default)
- Use furniture items (placed furniture, not the same as fixed map furniture in some buildings):
  - TV
  - Telephone
  - Workbench
  - Farm Computer
  - Arcade Systems
  - Sewing Machine
  - Mini-Jukebox

#### More features!

- Hide the HUD while sitting - Optional, automatically displays HUD again after standing.
- Chatty villagers - Allows you to talk to villagers more than once while sitting (usually 1-3 more times).
- Mumble to yourself - Your farmer will occasionally talk to himself as he sits, and every now and then while sitting.
- Villager greetings - Nearby villagers and pets will emote at you as they pass or if you sit within range of them.

### Eating and fishing while sitting (experimental)

Unlike everything else on this list, these two don't use targeting at all - they just work off
whatever food/drink or fishing rod you already have selected in your toolbar, using your normal
action/use tool buttons exactly like standing:

- **Eating While Sitting:** with food or a drink selected and nothing targeted, the action button eats or drinks it. Food must be held above your head to eat (which is not visible while sitting), so either hold it and then sit, or change items in the toolbar and then switch back to the food.
- **Fishing While Sitting:** with a fishing rod equipped and nothing targeted, hold tool button to charge a cast and release to cast - hooking a bite and the whole catching
  minigame work exactly like they do while standing.

Both are off by default and marked experimental - they needed a lot of extra work to get right, so
enable them in settings if you want to try them, and please report anything weird you run into.

Note: the animations of eating and fishing will look glitchy as these were never intended to be used while sitting. I find them close enough personally to not warrant the extra work to replace them so they will be left as-is.

### Additional info

Only objects you're facing (directly ahead, diagonally ahead, or straight to either side) can be
targeted, except the Saloon bar, which works from any direction. Turn this off with "Require
Facing Target" in settings to allow targeting in any direction.

When gifting is disabled, if you try talking with an NPC with a giftable item selected, you will be told to store the gift before you can talk. If enabled, an item has to be held in order to gift it to the NPC and the text above the player will read "Give a gift to [name]".

Most of the features of this mod can be customized. It is highly recommended to use Generic Mod Config Menu (GMCM) with this mod. Without GMCM installed, settings can be hand-edited in `config.json` after the mod runs once.

Without GMCM installed, settings can be hand-edited in `config.json` after the mod runs once.

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
