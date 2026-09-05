using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using SitAndDoStuff.Targeting;
using StardewValley.Menus;
using StardewValley.Tools;

namespace SitAndDoStuff
{
    public class ModEntry : Mod
    {
        public ModConfig Config;

        // Null whenever the player hasn't started cycling since sitting down (equivalent to being
        // parked on "None"). See TargetSession.
        private TargetSession _session;

        // Categories that threw once this session get skipped afterward, so one broken interaction
        // (e.g. after a future game update) doesn't keep erroring or block everything else.
        private readonly HashSet<InteractionCategory> _disabledAfterError = new();

        private FlavorTextManager _flavorText;
        private PassingEmoteManager _passingEmotes;
        private RepeatChatTracker _repeatChatTracker;

        // Tracks sitting state one tick back, purely to detect the exact moment sitting starts -
        // needed so PassingEmoteManager.OnStartSitting() fires exactly once per sit, not every tick.
        private bool _wasSittingLastTick;

        // True only while OUR toggle hid the HUD, so we don't restore it if something else hid it.
        private bool _hudHiddenByMod;

        // True while THIS mod's own eating-while-sitting animation is in flight - scopes the
        // Halt_Prefix patch below to just that, never other mods or vanilla's own calls. Cleared by
        // OnUpdateTicked once the animation ends.
        private static bool _ourHeldActionAnimationInProgress;

        // Whether isEating has read true yet since the flag above was armed. Needed because isEating
        // reads false both before the eat prompt is answered and after the animation finishes - this
        // tells the two apart so OnUpdateTicked doesn't disarm the fix too early.
        private static bool _heldActionEatingHasStarted;

        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();
            _flavorText = new FlavorTextManager(helper);
            _repeatChatTracker = new RepeatChatTracker();
            _passingEmotes = new PassingEmoteManager();

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.Input.ButtonsChanged += OnButtonsChanged;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.RenderingWorld += OnRenderingWorld;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
            helper.Events.Player.Warped += (_, _) => ClearSession();

            var harmony = new Harmony(ModManifest.UniqueID);
            // Vanilla calls Game1.player.Halt() every tick while idle (Game1.UpdateControlInput).
            // Harmless while standing, but for a sitting player it re-fires ShowSitting() ->
            // setCurrentSingleFrame() on every one of those ticks, which replaces the eating
            // animation's frame list mid-playback without resetting its index - permanently breaking
            // the index/list-length relationship the animation needs to ever reach its own completion
            // check, so Farmer.doneEating() never runs. Confirmed from decompiled source and a
            // Harmony stack-trace prefix on Halt() (see ai/known-issues.md Issue 2).
            //
            // Fix: skip Halt()'s body only while our own eating animation is genuinely in progress
            // (_ourHeldActionAnimationInProgress), so this can't affect any other mod or vanilla path.
            harmony.Patch(
                original: AccessTools.Method(typeof(Farmer), nameof(Farmer.Halt)),
                prefix: new HarmonyMethod(typeof(ModEntry), nameof(Halt_Prefix)));

            Monitor.Log($"Sit and Do Stuff loaded. Built against Stardew Valley 1.6.15 / SMAPI 4.x. " +
                        $"Running under SMAPI {Constants.ApiVersion} on game version {Game1.version}.", LogLevel.Trace);
        }

        // Skips Farmer.Halt()'s body only while our own eating animation is genuinely in progress
        // while sitting (see the patch comment in Entry()). Every other call runs unmodified.
        private static bool Halt_Prefix(Farmer __instance)
        {
            if (_ourHeldActionAnimationInProgress && __instance.IsSitting() && __instance.FarmerSprite.PauseForSingleAnimation)
                return false;

            return true;
        }

        public void SaveConfig() => Helper.WriteConfig(Config);

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm != null)
                GmcmMenuBuilder.Register(gmcm, ModManifest, this);
            else
                Monitor.Log("Generic Mod Config Menu not found — the mod will still work, but you'll need to edit config.json by hand to change settings.", LogLevel.Info);
        }

        // Drops any leftover session once sitting stops, restores the HUD if we hid it, and drives
        // flavor text, passing emotes, and the sitting-pose reassertion below.
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady)
            {
                ClearSession();
                RestoreHudIfNeeded();
                _flavorText.Reset();
                _passingEmotes.Reset();
                _repeatChatTracker.Reset();
                _wasSittingLastTick = false;
                return;
            }

            bool isSitting = Game1.player.IsSitting();

            if (isSitting && !_wasSittingLastTick)
            {
                // Just sat down this tick - arm the "first check" special case in PassingEmoteManager.
                _passingEmotes.OnStartSitting();
            }

            if (!isSitting)
            {
                ClearSession();
                RestoreHudIfNeeded();
                _passingEmotes.Reset();
                _repeatChatTracker.Reset();
            }
            else if (_session != null && Game1.player.currentLocation != _session.Location)
            {
                // Defensive: sitting in a different location than the session was built for (e.g. a
                // warp that didn't fire the Warped event).
                ClearSession();
            }
            else if (_session != null && !TargetHighlightsEnabled())
            {
                // All three visual indicators were just turned off - with no way to see what's
                // targeted, cycling is effectively a disabled feature (see OnButtonsChanged); drop
                // any selection already made before the settings changed.
                ClearSession();
            }

            bool flavorTextEligible = isSitting && Game1.activeClickableMenu == null
                && !Game1.dialogueUp && !Game1.eventUp && Game1.currentMinigame == null;
            _flavorText.Update(Config, flavorTextEligible);

            if (isSitting)
                _passingEmotes.Update(Game1.player, Game1.player.currentLocation, Config);

            // Several vanilla paths (the fishing bite reaction, completelyStopAnimatingOrDoingAction
            // at the end of eating/fishing) leave the sprite on a standing frame while sitting
            // position offsets still apply, since none of them know sitting exists. Reasserting the
            // pose fixes all of them at once - but only during genuine idle sitting or the BobberBar
            // minigame, since reasserting more broadly fights the eating prompt's own faceDirection
            // call and the fishing charge animation (see ai/known-issues.md).
            bool inBobberBar = Game1.activeClickableMenu is BobberBar;
            bool shouldReassertSitting = isSitting
                && !Game1.player.FarmerSprite.PauseForSingleAnimation
                && (inBobberBar || (!Game1.player.UsingTool && Game1.activeClickableMenu == null));

            if (shouldReassertSitting)
            {
                Game1.player.ShowSitting();
            }

            CheckFishingChargeRelease(isSitting);

            // Disarm the Halt() fix once the eating attempt it's guarding is over: the player stood
            // up, the prompt closed without eating starting, or eating started and then finished.
            // isEating alone can't tell "not started yet" apart from "finished" - both read false -
            // hence the separate _heldActionEatingHasStarted flag.
            if (_ourHeldActionAnimationInProgress)
            {
                if (Game1.player.isEating)
                {
                    _heldActionEatingHasStarted = true;
                }
                else if (!isSitting || _heldActionEatingHasStarted || !Game1.dialogueUp)
                {
                    Monitor.Log("Held-action animation finished (or ended without starting, or sitting ended) - Halt() fix no longer engaged.", LogLevel.Debug);
                    _ourHeldActionAnimationInProgress = false;
                    _heldActionEatingHasStarted = false;
                }
            }

            _wasSittingLastTick = isSitting;
        }

        // Polls the real button state every tick instead of trusting ButtonsChanged's Released set:
        // suppressing the charge-start press corrupts SMAPI's own input layer for the rest of the
        // hold (confirmed by testing - see ai/known-issues.md Issue 1), so IsUseToolButtonHeld below
        // reads raw hardware state instead. Only applies while sitting.
        private void CheckFishingChargeRelease(bool isSitting)
        {
            if (!isSitting || !(Game1.player.CurrentTool is FishingRod rod) || !rod.isTimingCast)
                return;

            if (IsUseToolButtonHeld())
                return;

            SafeTryHeldAction("cast the fishing rod", () =>
            {
                HeldActionHandler.ReleaseFishingCast(Game1.player, Helper.Reflection);
                return true;
            });
        }

        private bool IsUseToolButtonHeld()
        {
            GamePadState padState = GamePad.GetState(PlayerIndex.One);
            if (padState.IsButtonDown(Buttons.X))
                return true;

            KeyboardState kbState = Keyboard.GetState();
            MouseState mouseState = Mouse.GetState();

            foreach (InputButton ib in Game1.options.useToolButton)
            {
                if (ib.mouseLeft && mouseState.LeftButton == ButtonState.Pressed)
                    return true;
                if (ib.mouseRight && mouseState.RightButton == ButtonState.Pressed)
                    return true;
                if (ib.key != Keys.None && kbState.IsKeyDown(ib.key))
                    return true;
            }
            return false;
        }

        // Only ever restores the HUD if OUR toggle was what hid it - never touches Game1.displayHUD
        // otherwise, so this can't interfere with some other mod (or the game itself) managing it.
        private void RestoreHudIfNeeded()
        {
            if (_hudHiddenByMod)
            {
                Game1.displayHUD = true;
                _hudHiddenByMod = false;
            }
        }

        // Controls don't use dedicated keybinds (aside from Toggle HUD) - each action maps onto
        // whichever of the player's own vanilla buttons does the closest thing while standing.
        //
        // The mapping, while sitting:
        //   - Something targeted: Action = interact with it. Use Tool = stand up.
        //   - Nothing targeted, food/drink selected: Action = eat. Use Tool = stand up.
        //   - Nothing targeted, fishing rod selected: Use Tool = cast/reel/hook like standing (only
        //     the first press, which starts the charge, is ours - see below for why the rest is safe
        //     to leave unsuppressed). Action = stand up.
        //   - Nothing targeted, neither selected: either button stands up.
        //   - Cycling always works via the player's own movement keys.
        private void OnButtonsChanged(object sender, ButtonsChangedEventArgs e)
        {
            // IsMouseOverUi() guards against a real mouse click on the toolbar - meaningless for
            // gamepad, which selects toolbar items by button, not cursor. But answering a gamepad
            // question dialogue (e.g. an Arcade System prompt) via SnappyMenus can leave the virtual
            // cursor parked over the toolbar permanently, making this check silently block everything
            // afterward (see ai/known-issues.md Issue 3). Skipped while gamepadControls is true.
            if (!Context.IsWorldReady || Game1.activeClickableMenu != null || Game1.dialogueUp || Game1.eventUp || Game1.currentMinigame != null
                || (!Game1.options.gamepadControls && IsMouseOverUi()))
                return;

            Farmer player = Game1.player;
            if (!player.IsSitting())
                return;

            GameLocation location = player.currentLocation;

            // ---- Cycling: available regardless of what's targeted, but only if at least one
            // ---- visual indicator is on - with all three off, there'd be no way to see what (if
            // ---- anything) got targeted, so cycling is treated as a disabled feature instead.
            bool highlightsEnabled = TargetHighlightsEnabled();
            bool cycleLeft = highlightsEnabled && e.Pressed.Any(MatchesMoveLeftButton);
            bool cycleRight = highlightsEnabled && e.Pressed.Any(MatchesMoveRightButton);
            if (cycleLeft || cycleRight)
            {
                Func<SButton, bool> cycleMatcher = cycleLeft ? MatchesMoveLeftButton : MatchesMoveRightButton;
                SuppressMatching(e, cycleMatcher);

                if (_session == null || !_session.HasSelection)
                {
                    List<InteractionTarget> targets;
                    try
                    {
                        targets = TargetFinder.FindTargets(player, location, Config, Monitor, _disabledAfterError, _repeatChatTracker);
                    }
                    catch (Exception ex)
                    {
                        Monitor.Log($"Failed to scan for nearby interactables: {ex.Message}", LogLevel.Error);
                        Monitor.Log(ex.ToString(), LogLevel.Trace);
                        Game1.showRedMessage("Sit and Do Stuff hit an error — see the SMAPI console for details.");
                        return;
                    }

                    _session = new TargetSession(targets, location, player.Tile, player.FacingDirection);
                }

                if (_session.Targets.Count == 0)
                {
                    Game1.playSound("cancel");
                    return;
                }

                bool hadSelectionBefore = _session.HasSelection;

                if (cycleLeft)
                    _session.MoveLeft();
                else
                    _session.MoveRight();

                Game1.playSound(hadSelectionBefore && !_session.HasSelection ? "dwop" : "shwip");
                return;
            }

            // ---- Toggle HUD: still its own dedicated (optional) keybind. ----
            if (Config.ToggleHudButton.JustPressed())
            {
                Helper.Input.SuppressActiveKeybinds(Config.ToggleHudButton);
                Game1.displayHUD = !Game1.displayHUD;
                _hudHiddenByMod = !Game1.displayHUD;
                return;
            }

            // ---- Action / Use Tool: everything else. ----
            bool actionPressed = e.Pressed.Any(MatchesActionButton);
            bool useToolPressed = e.Pressed.Any(MatchesUseToolButton);

            bool hasTarget = _session != null && _session.HasSelection;

            if (hasTarget)
            {
                if (actionPressed)
                {
                    SuppressMatching(e, MatchesActionButton);
                    InteractionTarget target = _session.Current;
                    ClearSession();
                    TryInvoke(target, player);
                }
                else if (useToolPressed)
                {
                    SuppressMatching(e, MatchesUseToolButton);
                    StandUpUnlessMidAnimation(player);
                }
                return;
            }

            bool foodSelected = Config.AllowEatingWhileSitting && player.ActiveObject != null && player.ActiveObject.Edibility != -300;
            bool rodSelected = Config.AllowFishingWhileSitting && player.CurrentTool is FishingRod;

            if (foodSelected && !rodSelected)
            {
                if (actionPressed)
                {
                    SuppressMatching(e, MatchesActionButton);
                    // Arms the Halt_Prefix fix for this eating attempt - safe to set before the
                    // prompt is answered; OnUpdateTicked disarms it once it's no longer needed.
                    if (SafeTryHeldAction("eat", () => HeldActionHandler.TryEat(player, Monitor)))
                        _ourHeldActionAnimationInProgress = true;
                }
                else if (useToolPressed)
                {
                    SuppressMatching(e, MatchesUseToolButton);
                    StandUpUnlessMidAnimation(player);
                }
            }
            else if (rodSelected)
            {
                if (useToolPressed && !player.UsingTool)
                {
                    // Starting a fresh charge. Game1.pressUseToolButton only redirects to standing up
                    // while !UsingTool, so this first press is the only one vanilla would intercept -
                    // we suppress it and start the charge ourselves.
                    //
                    // Gamepad is the exception: suppressing it causes FishingRod.tickUpdate to
                    // self-release the charge within the same tick via its own corrupted input read
                    // (see ai/known-issues.md Issue 1). Instead we leave it unsuppressed and rely on
                    // BeginFishingCharge setting UsingTool=true before the game's Update() runs -
                    // vanilla's own !UsingTool gate then skips the press on its own.
                    bool viaGamepad = e.Pressed.Contains(SButton.ControllerX);
                    if (!viaGamepad)
                        SuppressMatching(e, MatchesUseToolButton);

                    SafeTryHeldAction("start fishing", () => HeldActionHandler.BeginFishingCharge(player, Helper.Reflection));
                }
                // Release isn't handled via useToolReleased - suppressing the press above also
                // corrupts SMAPI's release tracking for that button. Polled instead every tick from
                // CheckFishingChargeRelease.
                else if (actionPressed)
                {
                    SuppressMatching(e, MatchesActionButton);
                    StandUpUnlessMidAnimation(player);
                }
                // else: UsingTool is already true and this isn't a release - left unsuppressed.
                // Vanilla's redirect no longer applies once UsingTool is true, so hooking a bite and
                // the BobberBar minigame run through the player's real buttons like normal.
            }
            else
            {
                if (actionPressed || useToolPressed)
                {
                    if (actionPressed)
                        SuppressMatching(e, MatchesActionButton);
                    if (useToolPressed)
                        SuppressMatching(e, MatchesUseToolButton);

                    StandUpUnlessMidAnimation(player);
                }
            }
        }

        // Standing up mid single-locked-animation (e.g. eating) leaves the sprite stuck:
        // PauseForSingleAnimation blocks normal walking animation but not StopSitting's position
        // lerp. Vanilla never hits this since those animations also set CanMove=false; our own
        // handling bypasses that, so check explicitly.
        //
        // Also refuses to stand up mid-fishing-charge (UsingTool + isTimingCast): nothing else ever
        // cancels an in-progress charge, so calling StopSitting() out from under one left the rod
        // stuck charging forever with the power bar still showing. Same fix shape as the animation
        // guard above - block the stand-up instead, so the player releases (which fires the cast)
        // or otherwise resolves it first, exactly like they'd have to while standing anyway.
        private void StandUpUnlessMidAnimation(Farmer player)
        {
            if (player.FarmerSprite.PauseForSingleAnimation)
                return;
            if (player.UsingTool && player.CurrentTool is FishingRod rod && rod.isTimingCast)
                return;

            ClearSession();
            player.StopSitting();
        }

        // Each InputButton entry is a keyboard key, mouseLeft, or mouseRight (never more than one).
        // SMAPI's SButton keyboard values are numerically identical to XNA's Keys, hence the cast.
        private static bool MatchesInputButtonList(InputButton[] buttons, SButton pressed)
        {
            foreach (InputButton ib in buttons)
            {
                if (ib.mouseLeft && pressed == SButton.MouseLeft)
                    return true;
                if (ib.mouseRight && pressed == SButton.MouseRight)
                    return true;
                if (ib.key != Keys.None && (SButton)(int)ib.key == pressed)
                    return true;
            }
            return false;
        }

        // Gamepad's role mapping is fixed, not player-configurable: A is always action, X is always
        // use-tool (Utility.mapGamePadButtonToKey).
        private static bool MatchesActionButton(SButton button)
        {
            if (button == SButton.ControllerA)
                return true;
            return MatchesInputButtonList(Game1.options.actionButton, button);
        }

        private static bool MatchesUseToolButton(SButton button)
        {
            if (button == SButton.ControllerX)
                return true;
            return MatchesInputButtonList(Game1.options.useToolButton, button);
        }

        // D-pad and the left stick both count as movement - SMAPI synthesizes LeftThumbstickLeft/
        // Right as regular button presses once the stick crosses its own deadzone, so this needs no
        // raw analog polling of its own.
        private static bool MatchesMoveLeftButton(SButton button)
        {
            if (button == SButton.DPadLeft || button == SButton.LeftThumbstickLeft)
                return true;
            return MatchesInputButtonList(Game1.options.moveLeftButton, button);
        }

        private static bool MatchesMoveRightButton(SButton button)
        {
            if (button == SButton.DPadRight || button == SButton.LeftThumbstickRight)
                return true;
            return MatchesInputButtonList(Game1.options.moveRightButton, button);
        }

        private void SuppressMatching(ButtonsChangedEventArgs e, Func<SButton, bool> matches)
        {
            foreach (SButton button in e.Pressed)
            {
                if (matches(button))
                    Helper.Input.Suppress(button);
            }
        }

        // Wraps a held-action call so a failure logs and gracefully does nothing, rather than
        // crashing the game.
        private bool SafeTryHeldAction(string actionName, Func<bool> action)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error while trying to {actionName} while sitting: {ex.Message}", LogLevel.Error);
                Monitor.Log(ex.ToString(), LogLevel.Trace);
                return false;
            }
        }

        // Vanilla's own fishing casting-power/distance bar draws as part of the normal world layer
        // (charging a cast isn't a Game1.currentMinigame, so flavor text's own minigame check doesn't
        // exclude it) - drawing our flavor text in the usual RenderedWorld pass, which fires AFTER
        // the world (and that bar) already drew, paints over it and hides the distance readout.
        // Drawing this one case earlier instead, before the world layer starts, lets vanilla's own
        // draw naturally paint over ours - see OnRenderedWorld below for the normal-case draw.
        private void OnRenderingWorld(object sender, RenderingWorldEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (IsChargingFishingCast())
                _flavorText.Draw(e.SpriteBatch, Game1.player);
        }

        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (_session != null && _session.HasSelection)
                TargetRenderer.Draw(e.SpriteBatch, _session.Current, Config);

            if (!IsChargingFishingCast())
                _flavorText.Draw(e.SpriteBatch, Game1.player);
        }

        private static bool IsChargingFishingCast() => Game1.player.CurrentTool is FishingRod rod && rod.isTimingCast;

        // Same check vanilla uses to decide whether a click hits UI or the world. Used so mouse-
        // based actions don't fire from clicking the toolbar or other on-screen UI.
        private bool IsMouseOverUi()
        {
            int x = Game1.getMouseX();
            int y = Game1.getMouseY();
            foreach (IClickableMenu menu in Game1.onScreenMenus)
            {
                if (menu != null && menu.isWithinBounds(x, y))
                    return true;
            }
            return false;
        }

        private void ClearSession() => _session = null;

        // Whether there's any way for the player to actually see what's targeted. Used to disable
        // cycling entirely when all three are off, rather than let them cycle "blind."
        private bool TargetHighlightsEnabled() => Config.ShowTargetHighlight || Config.ShowTargetArrow || Config.ShowTargetName;

        // Failures are caught and logged rather than crashing; the category is added to
        // _disabledAfterError so a broken TV, say, doesn't also block talking to NPCs.
        private void TryInvoke(InteractionTarget target, Farmer player)
        {
            try
            {
                target.Invoke(player);
            }
            catch (Exception ex)
            {
                _disabledAfterError.Add(target.Category);
                Monitor.Log(
                    $"Couldn't interact with '{target.DisplayName}' ({target.Category}) — this category will be " +
                    "skipped for the rest of this play session. This usually means a game update changed something " +
                    $"internally; please report this. Error: {ex.Message}",
                    LogLevel.Error);
                Monitor.Log(ex.ToString(), LogLevel.Trace);
                Game1.showRedMessage("Couldn't interact with that — see the SMAPI console for details.");
            }
        }
    }
}
