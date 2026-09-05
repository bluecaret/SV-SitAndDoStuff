using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Tools;

namespace SitAndDoStuff
{
    // Handles eating and fishing while sitting. Kept separate from Targeting/ because both work on
    // whatever the player already has SELECTED in their toolbar (Farmer.ActiveObject /
    // Farmer.CurrentTool), not on a nearby object - and both are triggered by ModEntry mapping the
    // player's own vanilla "Check/Do Action" and "Use Tool" buttons onto these, rather than a
    // dedicated keybind of our own. See ModEntry.OnButtonsChanged for the full mapping.
    //
    // Why this needs custom code at all: in vanilla, pressing the "use tool" button while sitting
    // (Game1.pressUseToolButton) is redirected straight into the same code that stands you up
    // (Game1.pressActionButton -> GameLocation.checkAction), before it ever reaches eating or
    // fishing. The methods below re-create just the pieces we need to get PAST that initial block,
    // copied directly from the decompiled game code - everything after that (hooking a bite,
    // reeling, the catching minigame) runs through vanilla's own normal handling untouched, since
    // Game1.pressUseToolButton's own redirect only applies while UsingTool is still false.
    public static class HeldActionHandler
    {
        // Attempts to eat/drink the player's currently selected item. Returns true if the prompt
        // was shown, false if nothing eatable is selected (or some other vanilla condition blocks
        // it right now). "monitor" is optional and purely for diagnostic logging - pass null to
        // skip it.
        //
        // Deliberately does NOT include the game's own FarmerSprite.setCurrentSingleAnimation(304)
        // call, unlike the original decompiled code this is based on. That call is a raw frame-set
        // that doesn't set PauseForSingleAnimation, so it doesn't get the same protection the real
        // eating animation (animateOnce, used once "Yes" is answered) gets - while sitting, it
        // would get silently overridden by the sitting pose every tick, with nothing to ever undo
        // it. Skipping it just means there's no brief "about to eat" pose in the moment before the
        // Yes/No prompt appears; the real eating animation afterward is unaffected.
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

            // Confirmed from decompiled Farmer.cs: answering "Yes" calls Farmer.eatHeldObject(),
            // which does NOT read itemToEat at all - it reads mostRecentlyGrabbedItem, reconciling
            // it against ActiveItem. That field is normally kept in sync with ActiveObject every
            // tick by Farmer.showCarrying() - but showCarrying() bails out immediately whenever
            // IsSitting() is true, so it's never synced while sitting and stays stale (often null).
            // eatHeldObject() then takes a "swap" path that overwrites the player's actual equipped
            // toolbar slot with that stale value and calls OnItemReceived(stale, stale.Stack, ...) -
            // throwing a NullReferenceException when stale is null, and corrupting the toolbar slot
            // as an uncaught side effect (this is what caused "things get glitchy after standing").
            // Setting this here mirrors exactly what showCarrying() would have done had the player
            // been standing, so eatHeldObject() finds ActiveItem/mostRecentlyGrabbedItem already
            // matching and never takes that path.
            player.mostRecentlyGrabbedItem = activeObject;

            if (Game1.objectData.TryGetValue(activeObject.ItemId, out var objectData))
            {
                bool isDrink = objectData.IsDrink && activeObject.preserve.Value.GetValueOrDefault() != Object.PreserveType.Pickle;
                string question = isDrink
                    ? Game1.content.LoadString("Strings\\StringsFromCSFiles:Game1.cs.3159", activeObject.DisplayName)
                    : Game1.content.LoadString("Strings\\StringsFromCSFiles:Game1.cs.3160", activeObject.DisplayName);

                // The game's own dialogue-answer dispatcher recognizes "Eat" and calls the real
                // Farmer.eatHeldObject() (which in turn calls eatObject()) when the player answers
                // "Yes" - that's what actually performs the animation, applies the item's effects,
                // and consumes it. We never call eatHeldObject()/eatObject() ourselves.
                Game1.currentLocation.createQuestionDialogue(question, Game1.currentLocation.createYesNoResponses(), "Eat");
            }

            return true;
        }

        // Starts charging a fishing cast, if a fishing rod is currently selected. Returns true if
        // charging started, false if there's no rod equipped (or something else prevents it right
        // now). This ISN'T a one-shot action - it only STARTS the charge; the actual cast fires
        // later, when the player releases the use-tool button - see ReleaseFishingCast() below.
        public static bool BeginFishingCharge(Farmer player, IReflectionHelper reflection)
        {
            if (!(player.CurrentTool is FishingRod rod))
                return false;
            if (player.UsingTool || Game1.dialogueUp || Game1.eventUp || player.canOnlyWalk || Game1.fadeToBlack)
                return false;

            player.lastClick = player.GetToolLocation();
            player.BeginUsingTool();

            // FishingRod's own tickUpdate has an auto-release check that watches the REAL mouse/
            // gamepad buttons to decide when the charge ends - but we're triggering this via
            // ModEntry's own button-matching, which those checks know nothing about. Forcing
            // usedGamePadToCast to true (via reflection, since it's private) disables the mouse
            // branch of that check entirely, so only our own explicit release below ends the charge.
            reflection.GetField<bool>(rod, "usedGamePadToCast").SetValue(true);

            return true;
        }

        // Called when the use-tool button is released while a fishing charge (started by
        // BeginFishingCharge above) is still in progress. Fires the actual cast at whatever power
        // the swinging meter had reached - exactly like releasing the real use-tool button does,
        // because this IS effectively that same release, just detected through our own matching
        // instead of vanilla's.
        public static void ReleaseFishingCast(Farmer player, IReflectionHelper reflection)
        {
            if (!(player.CurrentTool is FishingRod rod) || !rod.isTimingCast)
                return; // nothing to release - the charge already ended some other way, or wasn't fishing

            reflection.GetMethod(rod, "startCasting").Invoke();
        }
    }
}
