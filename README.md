# Sit and Do Stuff

A SMAPI mod for Stardew Valley. Compatible with SV 1.6.15+.

**Don't just sit there; do stuff! This mod lets you perform actions while sitting, such as watching TV, talking to
NPCs, petting animals, and more.**

Configurable via **Generic Mod Config Menu (GMCM)** (optional, but strongly recommended).

## How it works in-game

1. Sit down as normal. Nothing is targeted initially.
2. Cycle through targetable objects with the move left/right buttons.
   1. Cycling goes left to right relative to the way you're facing, wrapping back around to nothing targeted:
      `None -> Target 1 -> ... -> Target N -> None`.
   2. The highlight can be any combination of: a yellow square around the object's tile footprint, a small animated
      arrow above it, and the object's name above it.
3. Once targeting an object, use the action button like normal to interact.
4. To stand up while targeting, use the tool button.
5. With nothing targeted, either button just stands you up - unless you have food/a drink or a fishing rod selected
   and the matching experimental setting enabled, in which case the action button eats/drinks it, or the tool button
   charges/releases a fishing cast.

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

Unlike everything else on this list, these two don't use targeting at all - they just work off whatever food/drink or
fishing rod you already have selected in your toolbar, using your normal action/use tool buttons exactly like
standing:

- **Eating While Sitting:** with food or a drink selected and nothing targeted, the action button eats or drinks it.
  Food must be held above your head to eat (which is not visible while sitting), so either hold it and then sit, or
  change items in the toolbar and then switch back to the food.
- **Fishing While Sitting:** with a fishing rod equipped and nothing targeted, hold tool button to charge a cast and
  release to cast - hooking a bite and the whole catching minigame work exactly like they do while standing.

Both are off by default and marked experimental - they needed a lot of extra work to get right, so enable them in
settings if you want to try them, and please report anything weird you run into.

Note: the animations of eating and fishing will look glitchy as these were never intended to be used while sitting.
I find them close enough personally to not warrant the extra work to replace them so they will be left as-is.

### Additional info

Only objects you're facing (directly ahead, diagonally ahead, or straight to either side) can be targeted, except the
Saloon bar, which works from any direction. Turn this off with "Require Facing Target" in settings to allow targeting
in any direction.

When gifting is disabled, if you try talking with an NPC with a giftable item selected, you will be told to store the
gift before you can talk. If enabled, an item has to be held in order to gift it to the NPC and the text above the
player will read "Give a gift to [name]".

Most of the features of this mod can be customized. It is highly recommended to use Generic Mod Config Menu (GMCM)
with this mod. Without GMCM installed, settings can be hand-edited in `config.json` after the mod runs once. See
"Manual configuration" below for exactly what to change.

## FAQ

**Does it work in multiplayer? Does everyone in the farmhouse need it installed?**
It should work, though has not been tested in multiplayer. The mod only affects an individual player so
theoretically there shouldn't be a problem. However, each player who wants to use the mod will also need it
installed.

**Is it compatible with other mods?**
This mod touches minimal places that may conflict with other mods so there shouldn't be any compatibility issues.
That said there are a lot out there, if you come across a conflict let me know and I'll see what I can do.

**Does using this affect friendship or give me an unfair advantage?**
No. Talking, gifting, and petting all go through the exact same vanilla systems and daily limits as normal. Chatty
Villagers and Repeat Petting specifically show extra reactions without ever touching friendship points.

**Can I add or remove this mid-save?**
Yes. It doesn't store anything in your save file, just its own settings in `config.json`, so adding or removing it
at any point is safe.

**Does it work with a controller?**
Yes. The mod should work well with keyboard/mouse or controller.

**Do I need Generic Mod Config Menu (GMCM)?**
No, it's optional, but strongly recommended - without it, you're editing `config.json` by hand for every setting
change instead of using a menu.

**Why can't I set my own custom key for interacting/cycling/standing up?**
I didn't want to break muscle memory so I tried to use as much vanilla controls as possible in the mod so it feels
natural. This also leaves it very compatible with controllers as well as making the more stable. Toggle HUD is the
only feature with its own separate keybind.

**Why do the eating/fishing animations look a little glitchy while sitting?**
Because those animations were meant for a standing player and were never designed with sitting in mind. I think
they're close enough to still be worth having as an experimental feature, but they're not going to look perfectly
smooth.

**How can I support you?**
Share the mod around! You can also [Buy Me A Coffee](https://www.buymeacoffee.com/bluecaret).

## Manual configuration (without GMCM)

Every setting can be changed by hand-editing `config.json` if you don't have GMCM installed. The file lives at
`Mods/SitAndDoStuff/config.json`, and is created the first time you run the game with the mod installed. Edit it
with any plain text editor, and restart the game afterward - it's only read once, at launch, so changes won't take
effect until then.

Here's a default `config.json`, with every setting at its starting value:

```json
{
  "ToggleHudButton": "",
  "RequireFacingTarget": true,
  "ShowTargetName": true,
  "ShowTargetArrow": true,
  "ShowTargetHighlight": true,
  "HighlightColor": "#FFDE59",
  "AllowGiftingWhileSitting": false,
  "AllowRepeatChatDialogue": true,
  "AllowRepeatPetting": true,
  "FlavorTextFrequency": "Occasional",
  "AllowPassingEmotes": true,
  "AllowEatingWhileSitting": false,
  "AllowFishingWhileSitting": false,
  "TV": { "Enabled": true, "Range": 10, "MaxRange": 10 },
  "Telephone": { "Enabled": true, "Range": 1, "MaxRange": 10 },
  "ArcadeMachine": { "Enabled": true, "Range": 1, "MaxRange": 10 },
  "SewingMachine": { "Enabled": true, "Range": 1, "MaxRange": 10 },
  "FarmComputer": { "Enabled": true, "Range": 1, "MaxRange": 10 },
  "MiniJukebox": { "Enabled": true, "Range": 10, "MaxRange": 10 },
  "Workbench": { "Enabled": true, "Range": 1, "MaxRange": 10 },
  "NPC": { "Enabled": true, "Range": 5, "MaxRange": 10 },
  "Pet": { "Enabled": true, "Range": 5, "MaxRange": 20 },
  "FarmAnimal": { "Enabled": true, "Range": 10, "MaxRange": 20 },
  "SaloonBar": { "Enabled": true, "Range": 35, "MaxRange": 35 }
}
```

What each setting does:

- **ToggleHudButton**: a button name (e.g. `"Z"`, `"ControllerBack"`), or `""` for no binding. Toggles the HUD
  on/off while sitting.
- **RequireFacingTarget**: `true`/`false`. If `true`, you have to be facing toward something to target it. The
  Saloon bar always ignores this regardless.
- **ShowTargetName** / **ShowTargetArrow** / **ShowTargetHighlight**: `true`/`false` each, for the three visual
  indicators. Turning all three off disables cycling entirely.
- **HighlightColor**: a hex color code, e.g. `"#FFDE59"`.
- **AllowGiftingWhileSitting**: `true`/`false`. Whether interacting with a targeted NPC while holding a giftable
  item gives the gift instead of just talking.
- **AllowRepeatChatDialogue**: `true`/`false`. Extra villager dialogue.
- **AllowRepeatPetting**: `true`/`false`. Extra heart reaction after a pet's already been petted for the day.
- **FlavorTextFrequency**: one of `"Never"`, `"Infrequent"`, `"Occasional"`, `"Often"`. Allows the farmer to talk to
  themself while sitting.
- **AllowPassingEmotes**: `true`/`false`. NPCs will show an emote when you sit near them.
- **AllowEatingWhileSitting**: `true`/`false` Experimental.
- **AllowFishingWhileSitting**: `true`/`false` Experimental.
- **TV / Telephone / ArcadeMachine / SewingMachine / FarmComputer / MiniJukebox / Workbench / NPC / Pet /
  FarmAnimal / SaloonBar**: each is its own object with three fields:
  - `Enabled`: `true`/`false` - turns that category on or off entirely.
  - `Range`: how many tiles away it can be targeted from (1 = only the 8 tiles touching you).
  - `MaxRange`: the upper limit `Range` can be set to. Don't bother setting `Range` higher than this, it just gets
    clamped back down in-game anyway.

## How to build it

You'll need the game installed locally (Steam/GOG) plus SMAPI. This project uses `Pathoschild.Stardew.ModBuildConfig`,
which finds your game install automatically and copies the build output straight into your `Mods` folder.

1. Install the .NET 6 SDK.
2. Open a terminal in this folder and run:
   ```
   dotnet build
   ```
3. If the build package can't auto-detect your game path, create a `SitAndDoStuff.csproj.user` file (or set the
   `GamePath` MSBuild property / `GAME_PATH` environment variable) pointing at your Stardew Valley install folder.
   See the package's docs: https://www.nuget.org/packages/Pathoschild.Stardew.ModBuildConfig
4. On success, the mod is copied into your `Mods/SitAndDoStuff` folder automatically. Launch the game through SMAPI.

## Extending it

- To add a new "nearby object" interaction type: add a case to `InteractionCategory`, a `CategorySettings` property
  in `ModConfig` (plus a case in its `Get()` switch), a settings-menu entry in `GMCMIntegration`'s `categories`
  list, and a scanning block in `TargetFinder.FindTargets`.
- To recognize an object under a different name (e.g. a mod-added reskin), adjust the string matches in
  `TargetFinder.ClassifyObject`.

## Disclaimer and credits

This is my first mod, and I'm new to C#, therefore I did use AI to create the initial mod code so that I could have
a jumpstart and a good foundation for how mods work. Further work after the initial creation was hand-coded by me.

Developed by [BlueCaret](https://www.bluecaret.com/).

Check out my Stardew Valley companion app, [Gunther's Library](https://www.bluecaret.com/guntherslibrary/)

[Buy Me A Coffee](https://www.buymeacoffee.com/bluecaret)
