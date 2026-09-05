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

        // True only while the HUD is hidden because OUR toggle hid it - lets us tell that apart from
        // the HUD being hidden for some unrelated reason, so we only ever restore it ourselves when
        // it was ourselves who hid it.
        private bool _hudHiddenByMod;

        // Harmony patch methods must be static, so this gives them a way to log.
        private static IMonitor _diagnosticMonitor;

        // True only while THIS mod has a vanilla single-frame animation (currently just eating) in
        // flight while sitting - see the Halt_Prefix comment below for why this exists and what it
        // guards against. Never set true by anything other than this mod's own held-action code, and
        // always cleared the moment that animation is no longer in progress (see OnUpdateTicked) -
        // this is deliberately scoped so the Halt() patch is a complete no-op for any other mod's
        // code, or any vanilla path, that might independently produce the same
        // sitting+PauseForSingleAnimation combination.
        private static bool _ourHeldActionAnimationInProgress;

        // Tracks whether Game1.player.isEating has actually read true at least once since
        // _ourHeldActionAnimationInProgress was armed. Needed because isEating is still FALSE for
        // however long the Yes/No "eat this?" prompt sits unanswered - without this, OnUpdateTicked
        // had no way to tell "isEating is false because the prompt hasn't been answered yet" apart
        // from "isEating is false because the animation genuinely finished", and disarmed the fix one
        // tick after the prompt appeared instead of after the animation actually completed.
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
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
            helper.Events.Player.Warped += (_, _) => ClearSession();

            _diagnosticMonitor = Monitor;
            var harmony = new Harmony(ModManifest.UniqueID);
            // Confirmed from decompiled Game1.cs (UpdateControlInput): vanilla calls
            // Game1.player.Halt() every single tick whenever no movement key is held and the player
            // isn't using a tool - true for essentially the whole eating sequence, standing or
            // sitting (eating never sets UsingTool, unlike fishing). For a STANDING player this is
            // harmless: Farmer.Halt()'s own ShowSitting() call is gated on IsSitting(), so it never
            // fires. For a SITTING player, that gate passes on every one of these per-tick calls,
            // calling ShowSitting() -> FarmerSprite.setCurrentSingleFrame() - which replaces the
            // underlying animation-frame list out from under the eating animation while it's still
            // mid-playback, without resetting the animation index. That permanently breaks the
            // index/list-length relationship FarmerSprite.currentAnimationTick() depends on to ever
            // advance again, which is why the eating animation's own completion check
            // (in animateOnce()) never passes and Farmer.doneEating() never runs - confirmed by two
            // earlier diagnostic patches on doneEating()/doneWithAnimation() never firing at all,
            // and a stack-trace-logging prefix on Halt() (both since removed) showing every
            // problematic call originating from this one vanilla line, not from anything
            // eating-specific. This is a real vanilla gap: Halt() was never written with the
            // possibility of a legitimate single-frame animation running while sitting in mind.
            //
            // Fix: skip Halt()'s entire original body for this one narrow case. Deliberately scoped
            // to _ourHeldActionAnimationInProgress (see its own comment) rather than just
            // IsSitting() && PauseForSingleAnimation, so this can't affect any other mod's code or
            // any other cause of that same combination - it only ever engages while THIS mod's own
            // eating flow is actually in progress.
            harmony.Patch(
                original: AccessTools.Method(typeof(Farmer), nameof(Farmer.Halt)),
                prefix: new HarmonyMethod(typeof(ModEntry), nameof(Halt_Prefix)));

            Monitor.Log($"Sit and Do Stuff loaded. Built against Stardew Valley 1.6.15 / SMAPI 4.x. " +
                        $"Running under SMAPI {Constants.ApiVersion} on game version {Game1.version}.", LogLevel.Trace);
        }

        // Returning false skips Farmer.Halt()'s original body entirely. Only ever does so while this
        // mod's own held-action animation (eating) is genuinely in progress while sitting - see the
        // comment on the patch registration in Entry() for the full mechanism this prevents. Every
        // other call to Halt() (standing, idle sitting, anything not triggered by this mod) runs
        // completely unmodified.
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

        // Drops any leftover session the moment the player stops sitting, by any means, so the
        // next sit starts fresh at "None". Also restores the HUD if we're the one who hid it,
        // drives the flavor-text timer, drives passing-greeting emotes, and keeps the sitting pose
        // showing correctly through vanilla code paths that don't know sitting exists.
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

            bool flavorTextEligible = isSitting && Game1.activeClickableMenu == null
                && !Game1.dialogueUp && !Game1.eventUp && Game1.currentMinigame == null;
            _flavorText.Update(Config, flavorTextEligible);

            if (isSitting)
                _passingEmotes.Update(Game1.player, Game1.player.currentLocation, Config);

            // Several vanilla code paths (the fishing minigame's bite reaction, and - confirmed
            // from source - Farmer.completelyStopAnimatingOrDoingAction, called at the end of both
            // eating and the "you caught a fish!" sequence) can leave the sprite showing a standing
            // frame while sitting-specific position offsets are still applied, since none of them
            // are aware sitting is even a possibility. Continuously re-asserting the correct pose
            // catches all of these uniformly instead of chasing each one individually.
            //
            // Deliberately narrow about WHEN, though - this used to fire any time
            // PauseForSingleAnimation was false, which turned out to be too broad: it also fired
            // while the eating Yes/No prompt was up (fighting TryEat's own faceDirection call,
            // which the eating flow depends on afterward) and throughout the entire fishing charge
            // (fighting updateMovementAnimation's own FishingRod-priority branch, which runs before
            // it would ever reach the sitting check - our reassertion, from this completely
            // separate tick hook, didn't respect that same priority). Now it only fires during
            // genuine idle sitting, or - the one case it was actually built for - the BobberBar
            // catching minigame specifically.
            bool inBobberBar = Game1.activeClickableMenu is BobberBar;
            bool shouldReassertSitting = isSitting
                && !Game1.player.FarmerSprite.PauseForSingleAnimation
                && (inBobberBar || (!Game1.player.UsingTool && Game1.activeClickableMenu == null));

            if (shouldReassertSitting)
            {
                Game1.player.ShowSitting();
            }

            CheckFishingChargeRelease(isSitting);

            // _ourHeldActionAnimationInProgress (see its declaration, and Halt_Prefix) must be
            // cleared the moment the animation it's guarding is no longer in progress, so the Halt()
            // fix never stays engaged longer than the single eating attempt it's protecting. isEating
            // only reads true once the Yes/No prompt has actually been answered "Yes", so it's tracked
            // separately (_heldActionEatingHasStarted) to tell "hasn't started yet" apart from
            // "started, then genuinely finished" - both read isEating == false. Also clears on
            // standing up, or once the prompt closes without eating ever having started (answered
            // "No", or dismissed), as safety nets.
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

        // Polls the real button state directly every tick, rather than trusting
        // ButtonsChanged's Released set, to decide when a fishing charge (started via
        // HeldActionHandler.BeginFishingCharge) ends. Suppressing the charge-start press (needed
        // to block vanilla's own stand-up redirect) appears to corrupt more than just that one
        // event - confirmed by testing that even polling Helper.Input.GetState() for this same
        // button shows the identical "released instantly" symptom afterward, meaning SMAPI's own
        // input layer no longer reliably reports this button's state for the rest of the hold.
        // IsUseToolButtonHeld below reads the raw hardware state directly instead (bypassing
        // SMAPI's input layer entirely), which should be unaffected by anything we've suppressed.
        // Only applies while sitting - a standing player's own fishing charge is entirely
        // vanilla's own business and untouched here.
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

        // Controls in this mod deliberately don't use their own custom keybinds (aside from Toggle
        // HUD) - instead, every action is mapped onto whichever of the player's OWN vanilla
        // "Check/Do Action", "Use Tool", and movement buttons already do the closest matching thing
        // while standing. See MatchesActionButton/MatchesUseToolButton/MatchesMoveLeftButton/
        // MatchesMoveRightButton below for how each is detected.
        //
        // The mapping, while sitting:
        //   - Something targeted: Action = interact with it. Use Tool = stand up.
        //   - Nothing targeted, food/drink selected: Action = eat. Use Tool = stand up.
        //   - Nothing targeted, fishing rod selected: Use Tool = cast/reel/hook, exactly like
        //     standing (only the very first press, which starts the charge, is ours - everything
        //     after that runs completely unsuppressed, see the comment further down for why that's
        //     safe). Action = stand up.
        //   - Nothing targeted, neither selected: either button stands up.
        //   - Cycling always works via the player's own movement keys, since movement itself does
        //     nothing while sitting anyway.
        private void OnButtonsChanged(object sender, ButtonsChangedEventArgs e)
        {
            // Confirmed (see ai/known-issues.md Issue 3): IsMouseOverUi() exists to avoid conflicting
            // with a genuine mouse click on a persistent on-screen menu (the toolbar) - a concern that
            // doesn't apply to gamepad input, since a gamepad player selects toolbar items via
            // dedicated buttons, never by aiming a cursor. On gamepad, though, answering a question
            // dialogue (e.g. an Arcade System's continue/exit prompt) via SnappyMenus leaves the
            // virtual snap-cursor parked wherever the last response option was rendered - which can
            // land squarely inside the toolbar's bounds and stays there indefinitely, since nothing
            // ever moves a real mouse to reposition it. That made IsMouseOverUi() permanently true
            // afterward, silently blocking all sitting-interaction handling forever - independent of
            // activeClickableMenu/dialogueUp actually clearing correctly (confirmed they do). Skipping
            // the check while Game1.options.gamepadControls is true (the same flag vanilla itself uses
            // to know it's in gamepad mode) avoids this without touching keyboard/mouse behavior.
            if (!Context.IsWorldReady || Game1.activeClickableMenu != null || Game1.dialogueUp || Game1.eventUp || Game1.currentMinigame != null
                || (!Game1.options.gamepadControls && IsMouseOverUi()))
                return;

            Farmer player = Game1.player;
            if (!player.IsSitting())
                return;

            GameLocation location = player.currentLocation;

            // ---- Cycling: always available regardless of what's targeted. ----
            bool cycleLeft = e.Pressed.Any(MatchesMoveLeftButton);
            bool cycleRight = e.Pressed.Any(MatchesMoveRightButton);
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

            Monitor.Log($"OnButtonsChanged: hasTarget={hasTarget}, foodSelected={foodSelected}, rodSelected={rodSelected}, " +
                        $"actionPressed={actionPressed}, useToolPressed={useToolPressed}.", LogLevel.Debug);

            if (foodSelected && !rodSelected)
            {
                if (actionPressed)
                {
                    SuppressMatching(e, MatchesActionButton);
                    // Arms the Halt_Prefix fix (see its declaration) for the duration of this eating
                    // attempt. Safe to set even before "Yes" is answered (or if "No" is chosen) - the
                    // fix only actually engages once PauseForSingleAnimation is also true, which only
                    // happens once the real eating animation starts, and OnUpdateTicked disarms it
                    // again once the prompt closes without eating starting, or once eating starts and
                    // then finishes (see _heldActionEatingHasStarted).
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
                    // Starting a fresh charge. Confirmed from Game1.pressUseToolButton: its own
                    // "redirect to standing up while sitting" check is gated on !UsingTool, so this
                    // very first press is the ONLY one vanilla would otherwise intercept - we
                    // suppress just this one and start the charge ourselves instead.
                    //
                    // EXCEPT for a gamepad press (ControllerX): confirmed (see known-issues.md
                    // Issue 1) that for a real gamepad player, suppressing this press leads to
                    // vanilla's OWN FishingRod.tickUpdate self-releasing the charge within the same
                    // tick, via its gamepad-branch release check (which reads Game1.input - vanilla's
                    // own input view, corrupted by suppression for the rest of the hold - not
                    // anything this mod polls). Instead, for a gamepad press, we skip Suppress()
                    // entirely and rely on BeginFishingCharge below setting UsingTool=true before the
                    // game's own Update() runs later this same tick (OnButtonsChanged runs first) -
                    // vanilla's tool-dispatch call site is ALSO gated on !UsingTool (same fact as
                    // above), so it skips processing this press at all once UsingTool is already
                    // true, without suppression ever being involved.
                    bool viaGamepad = e.Pressed.Contains(SButton.ControllerX);
                    if (!viaGamepad)
                        SuppressMatching(e, MatchesUseToolButton);

                    SafeTryHeldAction("start fishing", () => HeldActionHandler.BeginFishingCharge(player, Helper.Reflection));
                }
                // Releasing to cast is NOT handled here via useToolReleased - suppressing the press
                // above appears to also corrupt SMAPI's event-based release tracking for that same
                // button, firing a release almost immediately regardless of how long it's actually
                // held. See CheckFishingChargeRelease, polled every tick from OnUpdateTicked
                // instead, which sidesteps that entirely.
                else if (actionPressed)
                {
                    SuppressMatching(e, MatchesActionButton);
                    StandUpUnlessMidAnimation(player);
                }
                // else: UsingTool is already true and this isn't a charge-release - deliberately
                // left completely unsuppressed. Once UsingTool is true, vanilla's own redirect no
                // longer applies (same !UsingTool gate as above), so hooking a bite and everything
                // inside the BobberBar minigame runs through the player's own real buttons exactly
                // as it would while standing - we don't need to replicate any of it ourselves.
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

        // Standing up mid-eating-animation (or any other single-locked animation) leaves the sprite
        // frozen afterward: PauseForSingleAnimation blocks updateMovementAnimation from ever
        // reaching the normal walking logic, but StopSitting's own position-lerp isn't gated the
        // same way, so the player visually "floats" with a stuck frame. Vanilla can never hit this
        // itself, since those animations also set CanMove=false, which blocks this kind of input
        // from registering in the first place - our own handling bypasses that, so we check
        // explicitly instead.
        private void StandUpUnlessMidAnimation(Farmer player)
        {
            if (player.FarmerSprite.PauseForSingleAnimation)
                return;

            ClearSession();
            player.StopSitting();
        }

        // Confirmed from Options.cs/InputButton.cs: actionButton/useToolButton/moveLeftButton/
        // moveRightButton are each an InputButton[], where every entry is EITHER a keyboard Keys
        // value, OR mouseLeft, OR mouseRight (never more than one). SMAPI's SButton keyboard values
        // are numerically identical to XNA's Keys values by design, so a raw cast is the standard
        // way to convert between them.
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

        // Confirmed from Utility.mapGamePadButtonToKey: gamepad's semantic role mapping is fixed
        // (not player-configurable the way keyboard/mouse are) - Buttons.A is always the action
        // role, Buttons.X is always the use-tool role, regardless of what's bound on keyboard/mouse.
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

        private static bool MatchesMoveLeftButton(SButton button)
        {
            if (button == SButton.DPadLeft)
                return true;
            return MatchesInputButtonList(Game1.options.moveLeftButton, button);
        }

        private static bool MatchesMoveRightButton(SButton button)
        {
            if (button == SButton.DPadRight)
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

        private void OnRenderedWorld(object sender, RenderedWorldEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (_session != null && _session.HasSelection)
                TargetRenderer.Draw(e.SpriteBatch, _session.Current, Config);

            _flavorText.Draw(e.SpriteBatch, Game1.player);
        }

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
