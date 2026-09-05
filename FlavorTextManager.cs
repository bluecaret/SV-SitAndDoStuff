using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;

namespace SitAndDoStuff
{
    // Shows an occasional "talking to yourself" message above the player while sitting, using the
    // same visual as NPC greeting bubbles. NPC.showTextAboveHead() only exists on NPC, so this
    // replicates its draw call (SpriteText.drawStringWithScrollCenteredAt) aimed at the player.
    //
    // Two line pools share this pipeline and MessageChance(): the recurring "flavor-text-N" pool
    // (a chance-roll every RecurringCheckIntervalMs while sitting) and the one-shot "sit-relief-N"
    // pool (a single roll ~0.5s after sitting down). "Never" disables both.
    public class FlavorTextManager
    {
        // IMPORTANT: update this to match however many "flavor-text-N" entries you actually have in
        // i18n/default.json - it does NOT auto-detect the count.
        private const int LineCount = 35;

        // Must match the number of "sit-relief-N" entries in i18n/default.json.
        private const int SitReliefLineCount = 20;

        private const double FadeInMs = 300;
        private const double HoldMs = 2500;
        private const double FadeOutMs = 500;

        // How long after sitting down the one-shot sit-relief check happens.
        private const double SitReliefDelayMs = 500;

        // How often the recurring chance-roll happens. Smaller = finer-grained, smoother pacing;
        // larger = coarser, more "clumped" feeling gaps between messages.
        private const double RecurringCheckIntervalMs = 5000;

        // SpriteText's own default is 3f; this shrinks the bubble to about two-thirds that size.
        // A plain constant, so feel free to tune it directly if you want it smaller/larger still.
        private const float BubbleFontPixelZoom = 2f;

        private readonly IModHelper _helper;

        private double _recurringCheckTimerMs;
        private int _lastLineIndex = -1;

        private double _sitReliefTimerMs;
        private bool _hasCheckedSitRelief;

        private string _currentText;
        private double _messageElapsedMs;

        public FlavorTextManager(IModHelper helper)
        {
            _helper = helper;
        }

        // Called every tick. "eligible" should be true only while sitting, with no menu/dialogue/
        // event active - same guard style used elsewhere in this mod.
        public void Update(ModConfig config, bool eligible)
        {
            if (!eligible)
            {
                Reset();
                return;
            }

            double elapsed = Game1.currentGameTime?.ElapsedGameTime.TotalMilliseconds ?? 0;

            // One-shot sit-relief check, ~0.5s after becoming eligible (i.e. after sitting down).
            // Only ever runs once per sit, regardless of whether it actually shows anything.
            if (!_hasCheckedSitRelief)
            {
                _sitReliefTimerMs += elapsed;
                if (_sitReliefTimerMs >= SitReliefDelayMs)
                {
                    _hasCheckedSitRelief = true;
                    if (config.FlavorTextFrequency != FlavorTextFrequency.Never && _currentText == null)
                    {
                        if (Game1.random.NextDouble() < MessageChance(config.FlavorTextFrequency))
                            ShowRandomSitReliefLine();
                    }
                }
            }

            if (_currentText != null)
            {
                _messageElapsedMs += elapsed;
                if (_messageElapsedMs >= FadeInMs + HoldMs + FadeOutMs)
                {
                    _currentText = null;
                    _messageElapsedMs = 0;
                }
                return;
            }

            if (config.FlavorTextFrequency == FlavorTextFrequency.Never)
                return;

            // Every RecurringCheckIntervalMs, roll the same per-tier chance as the sit-relief line -
            // a repeated coin-flip gives a more natural "sometimes clusters, sometimes gaps" feel
            // than a fixed randomized wait window.
            _recurringCheckTimerMs += elapsed;
            if (_recurringCheckTimerMs >= RecurringCheckIntervalMs)
            {
                _recurringCheckTimerMs = 0;
                if (Game1.random.NextDouble() < MessageChance(config.FlavorTextFrequency))
                    ShowRandomLine();
            }
        }

        // Clears pending timers so a queued message doesn't fire on the next sit, and re-arms the
        // one-shot sit-relief check.
        public void Reset()
        {
            _recurringCheckTimerMs = 0;
            _currentText = null;
            _messageElapsedMs = 0;
            _sitReliefTimerMs = 0;
            _hasCheckedSitRelief = false;
        }

        public void Draw(SpriteBatch b, Farmer player)
        {
            if (_currentText == null)
                return;

            float alpha = ComputeAlpha();
            if (alpha <= 0f)
                return;

            // Same positioning NPC.drawAboveAlwaysFrontLayer uses: above the character's standing
            // pixel, offset up by its sprite height (at 4x scale) plus some breathing room.
            Point standingPixel = player.StandingPixel;
            Vector2 local = Game1.GlobalToLocal(new Vector2(standingPixel.X, standingPixel.Y - player.Sprite.SpriteHeight * 4 - 64));

            // fontPixelZoom is a global static field affecting ALL SpriteText-drawn text - shrink it
            // just for this draw call, then restore it, same as SpriteText.cs does internally.
            float originalZoom = SpriteText.fontPixelZoom;
            SpriteText.fontPixelZoom = BubbleFontPixelZoom;
            SpriteText.drawStringWithScrollCenteredAt(b, _currentText, (int)local.X, (int)local.Y, "", alpha, null, 1, 1f);
            SpriteText.fontPixelZoom = originalZoom;
        }

        private void ShowRandomLine()
        {
            int index;
            do
            {
                index = Game1.random.Next(LineCount);
            } while (LineCount > 1 && index == _lastLineIndex);

            _lastLineIndex = index;
            _currentText = _helper.Translation.Get($"flavor-text-{index}");
            _messageElapsedMs = 0;
        }

        private void ShowRandomSitReliefLine()
        {
            int index = Game1.random.Next(SitReliefLineCount);
            _currentText = _helper.Translation.Get($"sit-relief-{index}");
            _messageElapsedMs = 0;
        }

        // Shared by both the recurring check and the sit-relief check.
        private static double MessageChance(FlavorTextFrequency frequency)
        {
            return frequency switch
            {
                FlavorTextFrequency.Often => 0.50,
                FlavorTextFrequency.Occasional => 0.20,
                FlavorTextFrequency.Infrequent => 0.08,
                _ => 0.20
            };
        }

        private float ComputeAlpha()
        {
            if (_messageElapsedMs < FadeInMs)
                return (float)(_messageElapsedMs / FadeInMs);
            if (_messageElapsedMs < FadeInMs + HoldMs)
                return 1f;

            double fadeOutElapsed = _messageElapsedMs - FadeInMs - HoldMs;
            return (float)Math.Max(0, 1 - fadeOutElapsed / FadeOutMs);
        }
    }
}