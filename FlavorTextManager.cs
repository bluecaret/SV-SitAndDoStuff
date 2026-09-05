using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace SitAndDoStuff
{
    // Shows an occasional "talking to yourself" message above the player while sitting. Uses a
    // small, quiet chat-bubble style (small font, tight box) rather than NPC's own greeting-bubble
    // style (the big blocky "shouting sign" scroll banner from SpriteText.drawStringWithScrollCenteredAt)
    // - a private aside should look and feel different from a shout, by design. See
    // DrawSmallTextBubble below for why this is a local reimplementation rather than a direct call
    // to vanilla's own SpriteText.drawSmallTextBubble.
    //
    // Two line pools share this pipeline and MessageChance(): the recurring "flavor-text-N" pool
    // (a chance-roll every RecurringCheckIntervalMs while sitting) and the one-shot "sit-relief-N"
    // pool (a single roll ~0.5s after sitting down). "Never" disables both.
    public class FlavorTextManager
    {
        // IMPORTANT: update this to match however many "flavor-text-N" entries you actually have in
        // i18n/default.json - it does NOT auto-detect the count.
        private const int LineCount = 101;

        // Must match the number of "sit-relief-N" entries in i18n/default.json.
        private const int SitReliefLineCount = 68;

        // How long a message stays on screen before disappearing. No fade in/out - it just shows
        // and then goes away, by preference.
        private const double DisplayMs = 2500;

        // Horizontal padding, in pixels each side, between the text and the bubble's edge.
        private const float HorizontalPaddingPx = 16f;

        // How far above the player's feet (StandingPixel) the bubble's bottom sits. Deliberately NOT
        // NPC's own formula (SpriteHeight*4 + 64 = 192px) - that's tuned for an upright STANDING
        // pose, which sits much taller than our SITTING player, and it anchors near the bubble's own
        // TOP (the old scroll style extended mostly downward from it) rather than its bottom (this
        // bubble extends upward from its anchor - see DrawSmallTextBubble) - both push the old
        // formula's result well above a seated player's head. Tuned empirically instead - adjust
        // freely if it still needs to move up or down.
        private const float BubbleHeightAboveStandingPixel = 108f;

        // How long after sitting down the one-shot sit-relief check happens.
        private const double SitReliefDelayMs = 500;

        // How often the recurring chance-roll happens. Smaller = finer-grained, smoother pacing;
        // larger = coarser, more "clumped" feeling gaps between messages.
        private const double RecurringCheckIntervalMs = 5000;

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
                if (_messageElapsedMs >= DisplayMs)
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

            Point standingPixel = player.StandingPixel;
            Vector2 bottomCenter = Game1.GlobalToLocal(new Vector2(standingPixel.X, standingPixel.Y - BubbleHeightAboveStandingPixel));

            DrawSmallTextBubble(b, _currentText, bottomCenter);
        }

        // Based on SpriteText.drawSmallTextBubble (confirmed from decompiled SpriteText.cs), but
        // reimplemented locally with wider horizontal padding (HorizontalPaddingPx) than vanilla's
        // own fixed 8px - vanilla's own spacing read as too tight around the text. Same box sprite,
        // same tail sprite, same font otherwise.
        private static void DrawSmallTextBubble(SpriteBatch b, string text, Vector2 positionOfBottomCenter)
        {
            Vector2 size = Game1.smallFont.MeasureString(text);

            float boxLeft = positionOfBottomCenter.X - size.X / 2f - HorizontalPaddingPx;
            float boxTop = positionOfBottomCenter.Y - size.Y;
            float boxWidth = size.X + HorizontalPaddingPx * 2f;
            float boxHeight = size.Y + 12f;

            IClickableMenu.drawTextureBox(b, Game1.mouseCursors_1_6, new Rectangle(241, 503, 9, 9),
                (int)boxLeft, (int)boxTop, (int)boxWidth, (int)boxHeight, Color.White, 4f, drawShadow: false, 1f);

            // The little pointer tail, pointing down toward the player (drawPointerOnTop: false case).
            b.Draw(Game1.mouseCursors_1_6, positionOfBottomCenter + new Vector2(-2.5f, 1f) * 4f,
                new Rectangle(251, 506, 5, 5), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1.00001f);

            Vector2 textPos = new Vector2(positionOfBottomCenter.X - size.X / 2f, boxTop + 8f);
            Utility.drawTextWithShadow(b, text, Game1.smallFont, textPos, Game1.textColor, 1f, 1.00002f, -1, -1, 0.5f);
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

    }
}