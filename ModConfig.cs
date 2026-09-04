using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using SitAndDoStuff.Targeting;

namespace SitAndDoStuff
{
    public enum FlavorTextFrequency
    {
        Never,
        Often,
        Occasional,
        Infrequent
    }

    // Enabled/Range settings shared by every "nearby object" category. One instance per category
    // in ModConfig below.
    public class CategorySettings
    {
        public bool Enabled { get; set; } = true;

        // Tiles. 1 = only the 8 tiles touching the player (including diagonals). Clamped between
        // 1 and MaxRange.
        public int Range { get; set; } = 1;

        // Upper bound for Range, both in GMCM's slider and in TargetFinder's actual distance check.
        // 10 by default; a category can override this (e.g. the Saloon bar) via the constructor.
        public int MaxRange { get; set; } = 10;

        public CategorySettings() { }

        public CategorySettings(bool enabled, int range, int maxRange = 10)
        {
            Enabled = enabled;
            Range = range;
            MaxRange = maxRange;
        }
    }

    // Saved as config.json by SMAPI; read/written live by Generic Mod Config Menu if installed.
    public class ModConfig
    {
        // ----------------------------------------------------------------
        // Controls
        // ----------------------------------------------------------------
        // Interacting, canceling sitting, cycling targets, eating, and fishing all use the
        // player's OWN vanilla "Check/Do Action", "Use Tool", and movement keys (from the game's
        // own Controls settings) instead of dedicated keybinds - see ModEntry.OnButtonsChanged for
        // the full mapping. Toggle HUD is the only action with a keybind of its own here.

        // No default binding - purely opt-in. Only does anything while sitting; otherwise the press
        // is left untouched (e.g. a key that normally opens the map will still open the map while
        // standing). Automatically restores the HUD if the player stands up while it's hidden.
        public KeybindList ToggleHudButton { get; set; } = new KeybindList();

        // ----------------------------------------------------------------
        // General behavior
        // ----------------------------------------------------------------

        // The Saloon bar always ignores this.
        public bool RequireFacingTarget { get; set; } = true;

        public bool ShowTargetName { get; set; } = true;
        public bool ShowTargetArrow { get; set; } = true;
        public bool ShowTargetHighlight { get; set; } = true;
        public string HighlightColor { get; set; } = "#FFDE59";

        // Off by default so gifting an NPC never happens accidentally just from confirming a
        // highlighted NPC without checking what's in hand.
        public bool AllowGiftingWhileSitting { get; set; } = false;

        // On by default. Once an NPC's real conversation friendship is already spent for the day,
        // this lets talking again pull a couple more genuine lines from their own dialogue data
        // (with zero further friendship impact) before falling back to "...". Off reverts to
        // vanilla's own behavior for repeat clicks.
        public bool AllowRepeatChatDialogue { get; set; } = true;

        // Occasional short "talking to yourself" messages while sitting, using the same speech-
        // bubble style as NPC greetings. "Never" turns the feature off entirely.
        public FlavorTextFrequency FlavorTextFrequency { get; set; } = FlavorTextFrequency.Occasional;

        // Nearby villagers/pets emote at the player while sitting (independent of whether the
        // Talk with NPCs category itself is enabled). Reuses NPC's and Pet's own Range/MaxRange
        // settings rather than adding separate ones.
        public bool AllowPassingEmotes { get; set; } = true;

        // ----------------------------------------------------------------
        // Experimental: eating and fishing while sitting
        // ----------------------------------------------------------------
        // These work completely differently from every other feature in this mod. Everything else
        // is about picking a nearby OBJECT to interact with. Eating and fishing are instead about
        // using whatever ITEM or TOOL the player already has selected in their toolbar - there's
        // nothing external to "target", which is why they're triggered by the vanilla Action/Use
        // Tool buttons (see ModEntry.OnButtonsChanged) instead of the cycle-and-select system. See
        // HeldActionHandler.cs for how they work.

        // Off by default. Press "Check/Do Action" (with nothing targeted) to eat/drink your
        // selected item.
        public bool AllowEatingWhileSitting { get; set; } = false;

        // Off by default. Press and hold "Use Tool" (with nothing targeted) to charge a fishing
        // cast, release to cast - just like vanilla.
        public bool AllowFishingWhileSitting { get; set; } = false;

        // ----------------------------------------------------------------
        // Per-category enable + range
        // ----------------------------------------------------------------

        public CategorySettings TV { get; set; } = new CategorySettings(true, 10);
        public CategorySettings Telephone { get; set; } = new CategorySettings(true, 1);
        public CategorySettings ArcadeMachine { get; set; } = new CategorySettings(true, 1);
        public CategorySettings SewingMachine { get; set; } = new CategorySettings(true, 1);
        public CategorySettings FarmComputer { get; set; } = new CategorySettings(true, 1);
        public CategorySettings MiniJukebox { get; set; } = new CategorySettings(true, 10);
        public CategorySettings Workbench { get; set; } = new CategorySettings(true, 1);
        public CategorySettings NPC { get; set; } = new CategorySettings(true, 5);
        public CategorySettings Pet { get; set; } = new CategorySettings(true, 5, maxRange: 20);
        public CategorySettings FarmAnimal { get; set; } = new CategorySettings(true, 10, maxRange: 20);
        public CategorySettings SaloonBar { get; set; } = new CategorySettings(true, 35, maxRange: 35);

        public CategorySettings Get(InteractionCategory category)
        {
            switch (category)
            {
                case InteractionCategory.TV: return TV;
                case InteractionCategory.Telephone: return Telephone;
                case InteractionCategory.ArcadeMachine: return ArcadeMachine;
                case InteractionCategory.SewingMachine: return SewingMachine;
                case InteractionCategory.FarmComputer: return FarmComputer;
                case InteractionCategory.MiniJukebox: return MiniJukebox;
                case InteractionCategory.Workbench: return Workbench;
                case InteractionCategory.NPC: return NPC;
                case InteractionCategory.Pet: return Pet;
                case InteractionCategory.FarmAnimal: return FarmAnimal;
                case InteractionCategory.SaloonBar: return SaloonBar;
                default: return new CategorySettings(false, 1);
            }
        }
    }
}