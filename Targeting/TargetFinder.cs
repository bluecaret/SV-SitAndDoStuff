using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Locations;
using StardewValley.Objects;
using SObject = StardewValley.Object;
// Aliased to avoid confusion with C#'s own "object" type.

namespace SitAndDoStuff.Targeting
{
    // Scans the current location and builds the list of everything the player can interact with
    // right now, given where they're sitting and which way they're facing. Called once by ModEntry
    // whenever a new TargetSession starts.
    public static class TargetFinder
    {
        // "monitor" lets individual failures be logged instead of crashing the whole scan.
        // "disabledCategories" is categories that already threw once this session and are skipped.
        // "repeatChatTracker" is per-sitting-session state for the NPC repeat-chat feature below.
        public static List<InteractionTarget> FindTargets(Farmer player, GameLocation location, ModConfig config,
            IMonitor monitor, ISet<InteractionCategory> disabledCategories, RepeatChatTracker repeatChatTracker)
        {
            var results = new List<InteractionTarget>();
            if (location == null)
                return results;

            Vector2 playerTile = player.Tile;
            int facing = player.FacingDirection; // 0 = up, 1 = right, 2 = down, 3 = left
            bool isSaloon = location.Name == "Saloon";

            // ---------------------------------------------------------
            // Placed objects/furniture: TV, telephone, arcade machines, sewing machine, farm
            // computer, mini-jukebox, workbench. Furniture is a subclass of Object, so both
            // location.Objects and location.furniture are scanned together.
            // ---------------------------------------------------------
            IEnumerable<SObject> allObjects = Enumerable.Empty<SObject>();
            SafeRun(monitor, "reading placed objects/furniture", () =>
            {
                allObjects = location.Objects.Values
                    .Concat(location.furniture.Cast<SObject>())
                    .ToList();
            });

            foreach (SObject obj in allObjects)
            {
                SafeRun(monitor, $"checking object '{obj?.Name}'", () =>
                {
                    if (obj == null)
                        return;

                    InteractionCategory? category = ClassifyObject(obj);
                    if (category == null || disabledCategories.Contains(category.Value))
                        return;

                    // Excludes the Saloon's own built-in arcade cabinets, not just player-placed ones.
                    if (category.Value == InteractionCategory.ArcadeMachine && isSaloon)
                        return;

                    CategorySettings settings = config.Get(category.Value);
                    if (!settings.Enabled)
                        return;

                    Vector2 tile = obj.TileLocation;

                    // Furniture (TVs, Arcade Systems, Mini-Jukeboxes especially) can span multiple
                    // tiles; plain bigcraftables are always 1x1.
                    int tilesWide = 1, tilesHigh = 1;
                    float extraHeightAboveFootprint;

                    // These four are hardcoded to a tuned height regardless of their underlying C#
                    // type - unlike TV/ArcadeMachine/MiniJukebox, which use the real calculated
                    // sprite height below whenever they're Furniture-typed.
                    bool useFixedHeight = category.Value == InteractionCategory.Telephone
                        || category.Value == InteractionCategory.FarmComputer
                        || category.Value == InteractionCategory.SewingMachine
                        || category.Value == InteractionCategory.Workbench;

                    if (obj is Furniture furn)
                    {
                        tilesWide = Math.Max(1, furn.getTilesWide());
                        tilesHigh = Math.Max(1, furn.getTilesHigh());

                        if (useFixedHeight)
                        {
                            extraHeightAboveFootprint = Game1.tileSize * 0.5f;
                        }
                        else
                        {
                            // Same formula Furniture.cs itself uses to compute drawPosition - how far
                            // the actual sprite extends above the footprint. sourceRect.Height is the
                            // sprite's raw source-sheet height; 4f matches the fixed scale
                            // Furniture.draw() uses.
                            float spriteHeightPixels = furn.sourceRect.Value.Height * 4f;
                            float footprintHeightPixels = tilesHigh * Game1.tileSize;
                            extraHeightAboveFootprint = Math.Max(0f, spriteHeightPixels - footprintHeightPixels);
                        }
                    }
                    else
                    {
                        // Not Furniture-typed at all, so there's no sourceRect to calculate from -
                        // always the flat approximation here, tuned to 1.5 tiles tall total.
                        extraHeightAboveFootprint = Game1.tileSize * 0.5f;
                    }

                    // Measure range/facing from the closest point on the footprint, not always the
                    // top-left anchor - otherwise a wide TV can read as out of range from one end.
                    Vector2 nearestPoint = NearestPointInFootprint(playerTile, tile, tilesWide, tilesHigh);

                    if (!IsInRange(playerTile, nearestPoint, settings.Range, settings.MaxRange))
                        return;
                    if (config.RequireFacingTarget && !IsFacing(playerTile, nearestPoint, facing))
                        return;

                    SObject capturedObj = obj; // avoid capturing the loop variable in the lambda below
                    results.Add(new InteractionTarget(
                        category.Value,
                        tile,
                        obj.DisplayName,
                        // checkForAction() is what the game itself calls on click - already knows
                        // how to open the TV menu, jukebox song list, crafting menu, etc.
                        who => capturedObj.checkForAction(who, false),
                        tilesWide,
                        tilesHigh,
                        extraHeightAboveFootprint
                    ));
                });
            }

            // ---------------------------------------------------------
            // NPCs (talk/gift). Both go through the same NPC.checkAction call - the game decides
            // which based on whether the player is holding a giftable item. Pets and monsters are
            // excluded (pets are handled separately below; monsters aren't handled at all).
            // ---------------------------------------------------------
            if (config.Get(InteractionCategory.NPC).Enabled && !disabledCategories.Contains(InteractionCategory.NPC))
            {
                List<NPC> characters = new();
                SafeRun(monitor, "reading NPCs in location", () => characters = location.characters.ToList());

                CategorySettings settings = config.Get(InteractionCategory.NPC);
                foreach (NPC npc in characters)
                {
                    SafeRun(monitor, $"checking NPC '{npc?.Name}'", () =>
                    {
                        if (npc == null || npc.IsMonster || npc is Pet || npc is Horse)
                            return;

                        Vector2 tile = npc.Tile;
                        if (!IsInRange(playerTile, tile, settings.Range, settings.MaxRange))
                            return;
                        if (config.RequireFacingTarget && !IsFacing(playerTile, tile, facing))
                            return;

                        NPC capturedNpc = npc;

                        // tryToReceiveActiveObject(probe: true) is the same public "would this be
                        // accepted?" check the game uses internally, run without side effects, so
                        // the label matches what will actually happen.
                        bool wouldGiveItem = player.ActiveObject != null
                            && capturedNpc.tryToReceiveActiveObject(player, probe: true);

                        bool giftingAllowed = config.AllowGiftingWhileSitting;
                        string label = (wouldGiveItem && giftingAllowed)
                            ? $"Give a gift to {capturedNpc.displayName}"
                            : $"Talk to {capturedNpc.displayName}";

                        // Standing NPC sprites are almost always taller than their single-tile
                        // footprint (confirmed from NPC.cs's own greeting-bubble positioning, which
                        // uses this same Sprite.SpriteHeight * 4 formula).
                        float extraHeightAboveFootprint = Math.Max(0f, capturedNpc.Sprite.SpriteHeight * 4f - Game1.tileSize);

                        results.Add(new InteractionTarget(
                            InteractionCategory.NPC,
                            tile,
                            label,
                            who =>
                            {
                                if (wouldGiveItem && !giftingAllowed)
                                {
                                    // Can't call checkAction() here - who.ActiveObject is still set,
                                    // so it would gift regardless of the label. No clean way to
                                    // suppress just that branch, so decline and explain instead.
                                    Game1.showRedMessage("Gifting is disabled while sitting. Unequip your item, or turn " +
                                                          "on \"Allow Gifting While Sitting\" in the settings menu.");
                                    return;
                                }

                                // Confirmed from NPC.grantConversationFriendship: vanilla itself
                                // refuses to grant a second round of conversation friendship the
                                // same day, gated on this exact check. Once that's already true (and
                                // the setting's on), work through up to three "families" of the
                                // NPC's own real dialogue - one per repeat click, tracked in memory
                                // for this sitting session only (RepeatChatTracker) - before
                                // settling on "...". Deliberately only ever picks ONE heart-level-
                                // appropriate line per family, never multiple tiers within a family,
                                // since e.g. Jas's low-heart "I don't know you" line would make no
                                // sense showing after her higher-heart lines in the same sitting.
                                if (config.AllowRepeatChatDialogue && who.hasPlayerTalkedToNPC(capturedNpc.Name))
                                {
                                    who.friendshipData.TryGetValue(capturedNpc.Name, out var friendship);
                                    int heartLevel = friendship != null ? friendship.Points / 250 : 0;
                                    int familyIndex = repeatChatTracker.GetNextFamilyIndex(capturedNpc);
                                    bool foundDialogue = false;

                                    if (familyIndex == 0)
                                    {
                                        // Same method (and same two-attempt, with/without seasonal
                                        // prefix, pattern) checkAction itself uses. Never touches
                                        // friendship on its own.
                                        foundDialogue = capturedNpc.checkForNewCurrentDialogue(heartLevel)
                                            || capturedNpc.checkForNewCurrentDialogue(heartLevel, noPreface: true);
                                    }
                                    else if (familyIndex == 1)
                                    {
                                        // Bare day-name keys, optionally with a heart-level threshold
                                        // or season prefix (e.g. "Mon", "Mon8", "summer_Mon") - a
                                        // separate, well-established generic-smalltalk convention
                                        // checkForNewCurrentDialogue never checks on its own, since
                                        // it only ever tries day-names combined with a location.
                                        Dialogue dayLine = TryGetGenericDayDialogue(capturedNpc, heartLevel, includeSeasonPrefix: true)
                                            ?? TryGetGenericDayDialogue(capturedNpc, heartLevel, includeSeasonPrefix: false);
                                        if (dayLine != null)
                                        {
                                            capturedNpc.CurrentDialogue.Push(dayLine);
                                            foundDialogue = true;
                                        }
                                    }
                                    // familyIndex == 2, or -1 (all three already tried this sitting
                                    // session), or a family that came up empty: falls through to "...".

                                    if (!foundDialogue)
                                    {
                                        // Literal text (not a translation key lookup) reads as
                                        // "nothing more to say" rather than the interaction silently
                                        // doing nothing.
                                        capturedNpc.CurrentDialogue.Push(new Dialogue(capturedNpc, null, "..."));
                                    }

                                    Game1.drawDialogue(capturedNpc);
                                    return;
                                }

                                capturedNpc.checkAction(who, location);
                            },
                            extraHeightAboveFootprintPixels: extraHeightAboveFootprint
                        ));
                    });
                }
            }

            // ---------------------------------------------------------
            // Pets. Pet is a subclass of NPC, so it uses the same checkAction call as above - kept
            // as its own category purely for separate Enabled/Range settings.
            // ---------------------------------------------------------
            if (config.Get(InteractionCategory.Pet).Enabled && !disabledCategories.Contains(InteractionCategory.Pet))
            {
                List<NPC> characters = new();
                SafeRun(monitor, "reading pets in location", () => characters = location.characters.ToList());

                CategorySettings settings = config.Get(InteractionCategory.Pet);
                foreach (NPC npc in characters)
                {
                    SafeRun(monitor, $"checking pet '{npc?.Name}'", () =>
                    {
                        if (!(npc is Pet pet))
                            return;

                        Vector2 tile = pet.Tile;
                        if (!IsInRange(playerTile, tile, settings.Range, settings.MaxRange))
                            return;
                        if (config.RequireFacingTarget && !IsFacing(playerTile, tile, facing))
                            return;

                        Pet capturedPet = pet;

                        results.Add(new InteractionTarget(
                            InteractionCategory.Pet,
                            tile,
                            capturedPet.displayName,
                            who =>
                            {
                                // Confirmed from Pet.checkAction: it checks this exact field/value
                                // itself, and simply returns false (no emote, no sound, nothing) if
                                // already petted today - it's a hard daily limit, unlike NPCs where
                                // something might still show. Replicate the same feedback ourselves
                                // without touching lastPetDay/grantedFriendshipForPet at all, so
                                // there's zero friendship impact.
                                bool alreadyPettedToday = capturedPet.lastPetDay.TryGetValue(who.UniqueMultiplayerID, out var lastDay)
                                    && lastDay == Game1.Date.TotalDays;

                                if (alreadyPettedToday)
                                {
                                    capturedPet.doEmote(20); // "heart" - same emote a normal pet gives
                                    capturedPet.playContentSound();
                                    return;
                                }

                                capturedPet.checkAction(who, location);
                            },
                            // Fixed rather than Sprite.SpriteHeight-based (unlike NPCs) - that
                            // computed value came out too tall for pets specifically.
                            extraHeightAboveFootprintPixels: Game1.tileSize * 0.25f
                        ));
                    });
                }
            }

            // ---------------------------------------------------------
            // Farm animals: on the Farm map directly, or inside a barn/coop (AnimalHouse). Petting
            // uses FarmAnimal.pet() directly rather than GameLocation.checkAction(), which would
            // just stand the player up (see AddSaloonBarTargets below for why).
            // ---------------------------------------------------------
            if (config.Get(InteractionCategory.FarmAnimal).Enabled && !disabledCategories.Contains(InteractionCategory.FarmAnimal))
            {
                SafeRun(monitor, "checking farm animals", () =>
                {
                    IEnumerable<FarmAnimal> animals = null;
                    if (location is Farm farm)
                        animals = farm.animals.Values;
                    else if (location is AnimalHouse animalHouse)
                        animals = animalHouse.animals.Values;

                    if (animals == null)
                        return;

                    CategorySettings settings = config.Get(InteractionCategory.FarmAnimal);
                    foreach (FarmAnimal animal in animals.ToList())
                    {
                        SafeRun(monitor, $"checking farm animal '{animal?.Name}'", () =>
                        {
                            if (animal == null)
                                return;

                            Vector2 tile = animal.Tile;
                            if (!IsInRange(playerTile, tile, settings.Range, settings.MaxRange))
                                return;
                            if (config.RequireFacingTarget && !IsFacing(playerTile, tile, facing))
                                return;

                            FarmAnimal capturedAnimal = animal;
                            results.Add(new InteractionTarget(
                                InteractionCategory.FarmAnimal,
                                tile,
                                capturedAnimal.displayName,
                                who => capturedAnimal.pet(who, false),
                                // Tuned to 1.25 tiles tall total - close enough for label/arrow
                                // positioning without needing exact per-animal-type sprite data.
                                extraHeightAboveFootprintPixels: Game1.tileSize * 0.25f
                            ));
                        });
                    }
                });
            }

            // ---------------------------------------------------------
            // Saloon bar: a map tile property, not an object or character. See
            // AddSaloonBarTargets below.
            // ---------------------------------------------------------
            if (config.Get(InteractionCategory.SaloonBar).Enabled && !disabledCategories.Contains(InteractionCategory.SaloonBar))
            {
                SafeRun(monitor, "checking Saloon bar tiles", () => AddSaloonBarTargets(results, location, config, playerTile));
            }

            return results;
        }

        // The bar counter is tagged with a Buildings-layer "Action" property equal to "Saloon"
        // (confirmed from decompiled GameLocation.checkAction). We call GameLocation.saloon()
        // directly rather than checkAction() itself, because checkAction()'s first lines stand the
        // player up if IsSitting() is true, before ever reaching this case.
        private static void AddSaloonBarTargets(List<InteractionTarget> results, GameLocation location,
            ModConfig config, Vector2 playerTile)
        {
            CategorySettings settings = config.Get(InteractionCategory.SaloonBar);
            int range = settings.Range;
            int px = (int)playerTile.X;
            int py = (int)playerTile.Y;

            for (int dx = -range; dx <= range; dx++)
            {
                for (int dy = -range; dy <= range; dy++)
                {
                    int x = px + dx;
                    int y = py + dy;
                    string action = location.doesTileHaveProperty(x, y, "Action", "Buildings");
                    if (action != "Saloon")
                        continue;

                    Vector2 tile = new Vector2(x, y);
                    if (!IsInRange(playerTile, tile, range, settings.MaxRange))
                        continue;

                    // Ordering ignores facing by design. saloon() doesn't use its tileLocation
                    // argument, so (0, 0) here is fine.
                    results.Add(new InteractionTarget(
                        InteractionCategory.SaloonBar,
                        tile,
                        "Order a drink",
                        who => location.saloon(new xTile.Dimensions.Location(0, 0)),
                        // No real sprite here (it's a map tile marker, not an object) - tuned to
                        // 1.75 tiles tall for a good-looking label/arrow position at the bar.
                        extraHeightAboveFootprintPixels: Game1.tileSize * 0.75f
                    ));
                    return;
                }
            }
        }

        // Mirrors the exact descending-heart-threshold pattern checkForNewCurrentDialogue itself
        // uses for location-based keys (hearts 10 down to 2, in steps of 2), applied instead to the
        // bare day-name family of keys.
        private static Dialogue TryGetGenericDayDialogue(NPC npc, int heartLevel, bool includeSeasonPrefix)
        {
            string dayName = Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth);
            string preface = (includeSeasonPrefix && Game1.season != 0) ? Game1.currentSeason + "_" : "";

            for (int hearts = 10; hearts >= 2; hearts -= 2)
            {
                if (heartLevel < hearts)
                    continue;

                Dialogue found = npc.TryGetDialogue(preface + dayName + hearts);
                if (found != null)
                    return found;
            }

            return npc.TryGetDialogue(preface + dayName);
        }

        // Matches by name rather than item ID, so this keeps working across game updates and with
        // mod-added reskins.
        private static InteractionCategory? ClassifyObject(SObject obj)
        {
            string name = obj.Name ?? "";

            if (name.IndexOf("Mini-Jukebox", StringComparison.OrdinalIgnoreCase) >= 0)
                return InteractionCategory.MiniJukebox;
            if (name.IndexOf("TV", StringComparison.OrdinalIgnoreCase) >= 0 && obj is Furniture)
                return InteractionCategory.TV;
            if (name.IndexOf("Telephone", StringComparison.OrdinalIgnoreCase) >= 0)
                return InteractionCategory.Telephone;
            if (name.IndexOf("Arcade", StringComparison.OrdinalIgnoreCase) >= 0)
                return InteractionCategory.ArcadeMachine;
            if (name.IndexOf("Sewing Machine", StringComparison.OrdinalIgnoreCase) >= 0)
                return InteractionCategory.SewingMachine;
            if (name.IndexOf("Farm Computer", StringComparison.OrdinalIgnoreCase) >= 0)
                return InteractionCategory.FarmComputer;
            if (name.IndexOf("Workbench", StringComparison.OrdinalIgnoreCase) >= 0)
                return InteractionCategory.Workbench;

            return null;
        }

        // Standard "closest point on a rectangle" trick: clamp the player's position into the
        // footprint's bounds.
        private static Vector2 NearestPointInFootprint(Vector2 playerTile, Vector2 anchor, int tilesWide, int tilesHigh)
        {
            float nearestX = Math.Max(anchor.X, Math.Min(playerTile.X, anchor.X + tilesWide - 1));
            float nearestY = Math.Max(anchor.Y, Math.Min(playerTile.Y, anchor.Y + tilesHigh - 1));
            return new Vector2(nearestX, nearestY);
        }

        // Chebyshev distance (diagonal counts the same as straight) so a range of 1 covers exactly
        // the 8 tiles touching the player, matching the range description in the settings menu.
        // Public so PassingEmoteManager can reuse the exact same range logic instead of duplicating it.
        public static bool IsInRange(Vector2 from, Vector2 to, int range, int maxRange)
        {
            int dx = Math.Abs((int)from.X - (int)to.X);
            int dy = Math.Abs((int)from.Y - (int)to.Y);
            return Math.Max(dx, dy) <= Math.Max(1, Math.Min(maxRange, range));
        }

        // True if targetTile is within the player's front 180 degrees (dot product of the facing
        // vector and the direction to the target is non-negative).
        public static bool IsFacing(Vector2 playerTile, Vector2 targetTile, int facingDirection)
        {
            if (playerTile == targetTile)
                return true;

            Vector2 facingVector = DirectionToVector(facingDirection);
            Vector2 toTarget = targetTile - playerTile;
            if (toTarget != Vector2.Zero)
                toTarget.Normalize();

            float dot = Vector2.Dot(facingVector, toTarget);
            return dot >= -0.0001f; // small epsilon for floating-point rounding at exactly 90 degrees
        }

        public static Vector2 DirectionToVector(int facingDirection)
        {
            switch (facingDirection)
            {
                case 0: return new Vector2(0, -1);  // up
                case 1: return new Vector2(1, 0);   // right
                case 2: return new Vector2(0, 1);   // down
                case 3: return new Vector2(-1, 0);  // left
                default: return new Vector2(0, 1);
            }
        }

        // Isolates a single failure so one bad object can't take down the whole scan. Trace level
        // since these are expected to be rare.
        private static void SafeRun(IMonitor monitor, string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                monitor?.Log($"Skipped a target while {what}: {ex.Message}", LogLevel.Trace);
            }
        }
    }
}