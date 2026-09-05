using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Tools;

namespace SitAndDoStuff
{
    // Handles eating and fishing while sitting, acting on whatever's already selected in the
    // toolbar (Farmer.ActiveObject / Farmer.CurrentTool). Triggered by ModEntry mapping the
    // player's own vanilla Action/Use Tool buttons onto these - see ModEntry.OnButtonsChanged.
    //
    // Fishing needs this to get past a real, confirmed sitting redirect inside vanilla's
    // pressUseToolButton (see ai/vanilla-api-reference.md) - only the first charge-start press;
    // everything after (hooking a bite, the catching minigame) runs through vanilla untouched once
    // UsingTool is true. Eating has no such redirect in vanilla - its dispatch (inside
    // pressActionButton) never checks IsSitting() at all. TryEat exists instead because this mod
    // always takes over the Action button itself while sitting, so its own targeting decides
    // interact-vs-eat-vs-stand-up rather than letting vanilla's pressActionButton run for that press.
    public static class HeldActionHandler
    {
        // Attempts to eat/drink the player's currently selected item. Returns true if the prompt
        // was shown, false if nothing eatable is selected (or some other vanilla condition blocks
        // it). "monitor" is optional, for diagnostic logging - pass null to skip it.
        //
        // Deliberately skips vanilla's FarmerSprite.setCurrentSingleAnimation(304) "about to eat"
        // pose: it doesn't set PauseForSingleAnimation, so while sitting it would get silently
        // overridden by the sitting pose every tick with nothing to undo it. The real eating
        // animation afterward is unaffected.
        public static bool TryEat(Farmer player, IMonitor monitor = null)
        {
            Object activeObject = player.ActiveObject;
            if (activeObject == null)
            {
                monitor?.Log("TryEat: nothing selected in toolbar.", LogLevel.Debug);
                return false;
            }
            if (activeObject.Edibility == -300)
            {
                monitor?.Log($"TryEat: '{activeObject.Name}' isn't edible (Edibility == -300).", LogLevel.Debug);
                return false;
            }
            if (player.isEating || Game1.dialogueUp || Game1.eventUp || player.canOnlyWalk
                || player.FarmerSprite.PauseForSingleAnimation || Game1.fadeToBlack)
            {
                monitor?.Log($"TryEat: blocked by a guard - isEating={player.isEating}, dialogueUp={Game1.dialogueUp}, " +
                             $"eventUp={Game1.eventUp}, canOnlyWalk={player.canOnlyWalk}, " +
                             $"PauseForSingleAnimation={player.FarmerSprite.PauseForSingleAnimation}, fadeToBlack={Game1.fadeToBlack}.",
                             LogLevel.Debug);
                return false;
            }

            if (player.team.SpecialOrderRuleActive("SC_NO_FOOD")
                && player.currentLocation is MineShaft mine && mine.getMineArea() == 121)
            {
                monitor?.Log("TryEat: blocked by the SC_NO_FOOD special order rule.", LogLevel.Debug);
                Game1.addHUDMessage(new HUDMessage(Game1.content.LoadString("Strings\\StringsFromCSFiles:Object.cs.13053"), 3));
                return false;
            }

            if (player.hasBuff("25") && !activeObject.HasContextTag("ginger_item"))
            {
                monitor?.Log("TryEat: blocked by the nauseous buff.", LogLevel.Debug);
                Game1.addHUDMessage(new HUDMessage(Game1.content.LoadString("Strings\\StringsFromCSFiles:Nauseous_CantEat"), 3));
                return false;
            }

            monitor?.Log($"TryEat: all guards passed for '{activeObject.Name}' - showing the eat/drink prompt.", LogLevel.Debug);

            player.faceDirection(2); // 2 = facing down; matches vanilla's own behavior here
            player.itemToEat = activeObject;

            // Answering "Yes" calls Farmer.eatHeldObject(), which reads mostRecentlyGrabbedItem, not
            // itemToEat. That field is normally synced by Farmer.showCarrying() every tick, but
            // showCarrying() bails out while sitting, leaving it stale (often null) - which makes
            // eatHeldObject() overwrite the player's equipped toolbar slot and crash with a
            // NullReferenceException. Setting it here mirrors what showCarrying() would have done
            // while standing, so eatHeldObject() finds it already matching (see ai/known-issues.md
            // Issue 2).
            player.mostRecentlyGrabbedItem = activeObject;

            if (Game1.objectData.TryGetValue(activeObject.ItemId, out var objectData))
            {
                bool isDrink = objectData.IsDrink && activeObject.preserve.Value.GetValueOrDefault() != Object.PreserveType.Pickle;
                string question = isDrink
                    ? Game1.content.LoadString("Strings\\StringsFromCSFiles:Game1.cs.3159", activeObject.DisplayName)
                    : Game1.content.LoadString("Strings\\StringsFromCSFiles:Game1.cs.3160", activeObject.DisplayName);

                // Vanilla's dialogue-answer dispatcher recognizes "Eat" and calls Farmer.eatHeldObject()
                // itself when answered "Yes" - we never call it directly.
                Game1.currentLocation.createQuestionDialogue(question, Game1.currentLocation.createYesNoResponses(), "Eat");
            }

            return true;
        }

        // Starts charging a fishing cast, if a rod is selected. Only starts the charge - the actual
        // cast fires later, on release, via ReleaseFishingCast() below.
        public static bool BeginFishingCharge(Farmer player, IReflectionHelper reflection)
        {
            if (!(player.CurrentTool is FishingRod rod))
                return false;
            if (player.UsingTool || Game1.dialogueUp || Game1.eventUp || player.canOnlyWalk || Game1.fadeToBlack)
                return false;

            player.lastClick = player.GetToolLocation();
            player.BeginUsingTool();

            // FishingRod's own tickUpdate auto-releases based on the real mouse/gamepad buttons,
            // which know nothing about our own button-matching. Forcing usedGamePadToCast true
            // disables its mouse-release branch, so only our explicit release below ends the charge.
            reflection.GetField<bool>(rod, "usedGamePadToCast").SetValue(true);

            return true;
        }

        // Fires the actual cast when the use-tool button is released mid-charge (started by
        // BeginFishingCharge above), at whatever power the meter had reached.
        public static void ReleaseFishingCast(Farmer player, IReflectionHelper reflection)
        {
            if (!(player.CurrentTool is FishingRod rod) || !rod.isTimingCast)
                return; // nothing to release

            reflection.GetMethod(rod, "startCasting").Invoke();
        }
    }
}
