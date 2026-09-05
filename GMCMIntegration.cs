using System;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using SitAndDoStuff.Targeting;

namespace SitAndDoStuff
{
    // Hand-copied subset of Generic Mod Config Menu's public API, so this mod can use GMCM without
    // a hard dependency on it. ModEntry fetches an implementation via Helper.ModRegistry.GetApi and
    // passes it in; if GMCM isn't installed, GetApi returns null and this is never called.
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);

        void AddSectionTitle(IManifest mod, Func<string> text, Func<string> tooltip = null);
        void AddParagraph(IManifest mod, Func<string> text);

        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue,
            Func<string> name, Func<string> tooltip = null, string fieldId = null);

        void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue,
            Func<string> name, Func<string> tooltip = null, int? min = null, int? max = null,
            int? interval = null, Func<int, string> formatValue = null, string fieldId = null);

        void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue,
            Func<string> name, Func<string> tooltip = null, string[] allowedValues = null,
            Func<string, string> formatAllowedValue = null, string fieldId = null);

        void AddKeybindList(IManifest mod, Func<KeybindList> getValue, Action<KeybindList> setValue,
            Func<string> name, Func<string> tooltip = null, string fieldId = null);

        void AddPageLink(IManifest mod, string pageId, Func<string> text, Func<string> tooltip = null);
        void AddPage(IManifest mod, string pageId, Func<string> pageTitle = null);
    }

    public static class GmcmMenuBuilder
    {
        public static void Register(IGenericModConfigMenuApi api, IManifest manifest, ModEntry mod)
        {
            ModConfig Config() => mod.Config;

            var rangeLabel = "Range (tiles)";
            var rangeTooltip = "1 = must be directly adjacent (including diagonals). Max ";

            api.Register(
                mod: manifest,
                reset: () => mod.Config = new ModConfig(),
                save: () => mod.SaveConfig()
            );

            // ---------------- Controls ----------------
            api.AddSectionTitle(manifest, () => "Controls");
            api.AddParagraph(manifest, () =>
                "Interacting, canceling sitting, cycling targets, eating, and fishing all use your own " +
                "\"Check/Do Action\", \"Use Tool\", and movement keys from the game's own Controls settings.");

            // ---------------- Target behavior ----------------
            api.AddSectionTitle(manifest, () => "Targeting objects for interaction");
            api.AddBoolOption(manifest,
                () => Config().RequireFacingTarget, v => Config().RequireFacingTarget = v,
                () => "Require facing target",
                () => "If on, you must be facing toward a target (front, sides, or diagonally forward) to select " +
                      "it. The Saloon bar always ignores this.");
            api.AddBoolOption(manifest,
                () => Config().ShowTargetName, v => Config().ShowTargetName = v,
                () => "Show target name",
                () => "While targeting, the target's name will display above it.");
            api.AddBoolOption(manifest,
                () => Config().ShowTargetArrow, v => Config().ShowTargetArrow = v,
                () => "Show target arrow",
                () => "While targeting, a spinning arrow will display above the target.");
            api.AddBoolOption(manifest,
                () => Config().ShowTargetHighlight, v => Config().ShowTargetHighlight = v,
                () => "Show target highlight",
                () => "While targeting, an outline will display around the target's footprint.");
            api.AddTextOption(manifest,
                () => Config().HighlightColor, v => Config().HighlightColor = v,
                () => "Target highlight color (hex)",
                () => "Use a HEX color code, e.g. #FFDE59");

            // ---------------- Additional features ----------------
            api.AddSectionTitle(manifest, () => "Additional features");
            api.AddTextOption(manifest,
                () => Config().FlavorTextFrequency.ToString(),
                v => Config().FlavorTextFrequency = Enum.TryParse(v, out FlavorTextFrequency f) ? f : FlavorTextFrequency.Occasional,
                () => "Mumble while sitting",
                () => "Allow the farmer to talk to themself while sitting. Choose how often to show these " +
                      "messages. 'Never' turns this off. If it is a villager's birthday, the first time you sit for the " +
                      "day the farmer will mention it.",
                allowedValues: new[] { "Never", "Often", "Occasional", "Infrequent" });
            api.AddBoolOption(manifest,
                () => Config().AllowPassingEmotes, v => Config().AllowPassingEmotes = v,
                () => "Passing greetings",
                () => "Nearby villagers/pets emote at you while sitting (once per character per sitting " +
                      "session). The range set for Villagers and Pets affect the NPCs who will greet you. Does not " +
                      "affect friendship.");
            api.AddKeybindList(manifest,
                () => Config().ToggleHudButton, v => Config().ToggleHudButton = v,
                () => "Hide HUD",
                () => "While sitting, toggles the game's HUD (toolbar, health/stamina, clock, money) on and " +
                      "off. Bind a button to enable this option. Automatically restores the HUD when you stand up.");
            
            // ---------------- Experimental: eating and fishing ----------------
            api.AddSectionTitle(manifest, () => "Eat/Fish (experimental)");
            api.AddBoolOption(manifest,
                () => Config().AllowEatingWhileSitting, v => Config().AllowEatingWhileSitting = v,
                () => "Allow Eating While Sitting",
                () => "With nothing targeted: press \"Check/Do Action\" to eat. " +
                        "Animations may be glitchy. This is an experimental feature.");
            api.AddBoolOption(manifest,
                () => Config().AllowFishingWhileSitting, v => Config().AllowFishingWhileSitting = v,
                () => "Allow Fishing While Sitting",
                () => "With nothing targeted: press and hold \"Use Tool\" to charge a fishing cast (release to cast). " +
                        "Animations may be glitchy. This is an experimental feature.");

            // ---------------- NPC settings ----------------
            api.AddSectionTitle(manifest, () => "Talk/Gift Villagers");
            api.AddBoolOption(manifest,
                () => Config().NPC.Enabled, v => Config().NPC.Enabled = v,
                () => "Talk with Villagers",
                () => "When enabled, Villagers can be targeted and talked to. See 'Give gifts' below for " +
                      "gift-giving.");
            api.AddNumberOption(manifest,
                () => Config().NPC.Range, v => Config().NPC.Range = Math.Max(1, Math.Min(Config().NPC.MaxRange, v)),
                () => rangeLabel,
                () => rangeTooltip + Config().NPC.MaxRange,
                min: 1, max: Config().NPC.MaxRange, interval: 1);
            api.AddBoolOption(manifest,
                () => Config().AllowRepeatChatDialogue, v => Config().AllowRepeatChatDialogue = v,
                () => "Chatty Villagers",
                () => "Allows talking to villagers additional times (if villager has more chats available based " +
                      "on context, and with no extra friendship gain). Once conversation has been exhausted (usually " +
                      "max of 3 additional chats) they will respond with '...'. Repeats each time you sit.");
            api.AddBoolOption(manifest,
                () => Config().AllowGiftingWhileSitting, v => Config().AllowGiftingWhileSitting = v,
                () => "Give gifts",
                () => "Requires 'Talk with Villagers'. Off by default: interacting always talks, and prompts you to " +
                      "unequip if you're holding something giftable. On: the label switches to 'Give a gift to " +
                      "[Name]' when applicable; unequip beforehand if you'd rather just talk.");

            // ---------------- Saloon Bar settings ----------------
            api.AddSectionTitle(manifest, () => "Order food at the Saloon");
            api.AddBoolOption(manifest,
                () => Config().SaloonBar.Enabled, v => Config().SaloonBar.Enabled = v,
                () => "Saloon Bar",
                () => "Allows ordering food at the Saloon bar while sitting in any direction (ignores 'Require " +
                      "facing target').");
            api.AddNumberOption(manifest,
                () => Config().SaloonBar.Range, v => Config().SaloonBar.Range = Math.Max(1, Math.Min(Config().SaloonBar.MaxRange, v)),
                () => rangeLabel,
                () => "1 = must be directly adjacent (including diagonals). Max " + Config().SaloonBar.MaxRange + 
                      ", which should reach anywhere in the Saloon.",
                min: 1, max: Config().SaloonBar.MaxRange, interval: 1);

            // ---------------- Pet settings ----------------
            api.AddSectionTitle(manifest, () => "Pets");
            api.AddBoolOption(manifest,
                () => Config().Pet.Enabled, v => Config().Pet.Enabled = v,
                () => "Petting",
                () => "Allows petting your pets (cats, dogs, turtles, excluding horse).");
            api.AddNumberOption(manifest,
                () => Config().Pet.Range, v => Config().Pet.Range = Math.Max(1, Math.Min(Config().Pet.MaxRange, v)),
                () => rangeLabel,
                () => rangeTooltip + Config().Pet.MaxRange,
                min: 1, max: Config().Pet.MaxRange, interval: 1);
            api.AddBoolOption(manifest,
                () => Config().AllowRepeatPetting, v => Config().AllowRepeatPetting = v,
                () => "Repeat Petting",
                () => "Allows petting a pet again after its daily pet is already used up. Does not affect friendship.");

            // ---------------- Farm Animals settings ----------------
            api.AddSectionTitle(manifest, () => "Farm Animals");
            api.AddBoolOption(manifest,
                () => Config().FarmAnimal.Enabled, v => Config().FarmAnimal.Enabled = v,
                () => "Petting",
                () => "Allows petting your farm animals (chickens, cows, etc).");
            api.AddNumberOption(manifest,
                () => Config().FarmAnimal.Range, v => Config().FarmAnimal.Range = Math.Max(1, Math.Min(Config().FarmAnimal.MaxRange, v)),
                () => rangeLabel,
                () => rangeTooltip + Config().FarmAnimal.MaxRange,
                min: 1, max: Config().FarmAnimal.MaxRange, interval: 1);

            // ---------------- TV settings ----------------
            api.AddSectionTitle(manifest, () => "Television");
            api.AddBoolOption(manifest,
                () => Config().TV.Enabled, v => Config().TV.Enabled = v,
                () => "Watch TV",
                () => "Allows watching TV while sitting.");
            api.AddNumberOption(manifest,
                () => Config().TV.Range, v => Config().TV.Range = Math.Max(1, Math.Min(Config().TV.MaxRange, v)),
                () => rangeLabel,
                () => rangeTooltip + Config().TV.MaxRange,
                min: 1, max: Config().TV.MaxRange, interval: 1);

            // ---------------- Mini-Jukebox settings ----------------
            api.AddSectionTitle(manifest, () => "Mini-Jukebox");
            api.AddBoolOption(manifest,
                () => Config().MiniJukebox.Enabled, v => Config().MiniJukebox.Enabled = v,
                () => "Use jukebox",
                () => "Allows using the mini-jukebox while sitting.");
            api.AddNumberOption(manifest,
                () => Config().MiniJukebox.Range, v => Config().MiniJukebox.Range = Math.Max(1, Math.Min(Config().MiniJukebox.MaxRange, v)),
                () => rangeLabel,
                () => rangeTooltip + Config().MiniJukebox.MaxRange,
                min: 1, max: Config().MiniJukebox.MaxRange, interval: 1);

            // ---------------- Telephone settings ----------------
            api.AddSectionTitle(manifest, () => "Telephone");
            api.AddBoolOption(manifest,
                () => Config().Telephone.Enabled, v => Config().Telephone.Enabled = v,
                () => "Use phone",
                () => "Allows using the telephone while sitting.");
            api.AddNumberOption(manifest,
                () => Config().Telephone.Range, v => Config().Telephone.Range = Math.Max(1, Math.Min(Config().Telephone.MaxRange, v)),
                () => rangeLabel,
                () => rangeTooltip + Config().Telephone.MaxRange,
                min: 1, max: Config().Telephone.MaxRange, interval: 1);

            // ---------------- Workbench settings ----------------
            api.AddSectionTitle(manifest, () => "Workbench");
            api.AddBoolOption(manifest,
                () => Config().Workbench.Enabled, v => Config().Workbench.Enabled = v,
                () => "Use workbench",
                () => "Allows using the workbench while sitting.");
            api.AddNumberOption(manifest,
                () => Config().Workbench.Range, v => Config().Workbench.Range = Math.Max(1, Math.Min(Config().Workbench.MaxRange, v)),
                () => rangeLabel,
                () => rangeTooltip + Config().Workbench.MaxRange,
                min: 1, max: Config().Workbench.MaxRange, interval: 1);

            // ---------------- Farm Computer settings ----------------
            api.AddSectionTitle(manifest, () => "Farm Computer");
            api.AddBoolOption(manifest,
                () => Config().FarmComputer.Enabled, v => Config().FarmComputer.Enabled = v,
                () => "Use farm computer",
                () => "Allows using the farm computer while sitting.");
            api.AddNumberOption(manifest,
                () => Config().FarmComputer.Range, v => Config().FarmComputer.Range = Math.Max(1, Math.Min(Config().FarmComputer.MaxRange, v)),
                () => rangeLabel,
                () => rangeTooltip + Config().FarmComputer.MaxRange,
                min: 1, max: Config().FarmComputer.MaxRange, interval: 1);

            // ---------------- Sewing Machine settings ----------------
            api.AddSectionTitle(manifest, () => "Sewing Machine");
            api.AddBoolOption(manifest,
                () => Config().SewingMachine.Enabled, v => Config().SewingMachine.Enabled = v,
                () => "Use sewing machine",
                () => "Allows using the sewing machine while sitting.");
            api.AddNumberOption(manifest,
                () => Config().SewingMachine.Range, v => Config().SewingMachine.Range = Math.Max(1, Math.Min(Config().SewingMachine.MaxRange, v)),
                () => rangeLabel,
                () => rangeTooltip + Config().SewingMachine.MaxRange,
                min: 1, max: Config().SewingMachine.MaxRange, interval: 1);

            // ---------------- Arcade System settings ----------------
            api.AddSectionTitle(manifest, () => "Arcade Systems");
            api.AddBoolOption(manifest,
                () => Config().ArcadeMachine.Enabled, v => Config().ArcadeMachine.Enabled = v,
                () => "Use arcade",
                () => "Allows using an Arcade System (placeable furniture, not the ones in the Saloon) while sitting.");
            api.AddNumberOption(manifest,
                () => Config().ArcadeMachine.Range, v => Config().ArcadeMachine.Range = Math.Max(1, Math.Min(Config().ArcadeMachine.MaxRange, v)),
                () => rangeLabel,
                () => rangeTooltip + Config().ArcadeMachine.MaxRange,
                min: 1, max: Config().ArcadeMachine.MaxRange, interval: 1);
            
            // ---------------- Thank you ----------------
            api.AddSectionTitle(manifest, () => "Thank you for using the mod!");
            api.AddParagraph(manifest, () => "This mod was developed by BlueCaret.com. Check out my companion app: " +
                                             "Gunther's Library! Available on iOS, Android, and web");
        }
    }
}
