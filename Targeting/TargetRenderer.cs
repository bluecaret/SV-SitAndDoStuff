using System;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace SitAndDoStuff.Targeting
{
    // Purely visual - draws the highlight square/arrow and name label over the current target.
    // Called once per frame from ModEntry via SMAPI's Display.RenderedWorld event.
    public static class TargetRenderer
    {
        // How far in from the literal screen edge the off-screen arrow/label sit.
        private const float EdgeMargin = 40f;

        public static void Draw(SpriteBatch b, InteractionTarget target, ModConfig config)
        {
            if (target == null)
                return;

            Vector2 worldPos = new Vector2(target.Tile.X * Game1.tileSize, target.Tile.Y * Game1.tileSize);
            Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos);

            float footprintPixelWidth = target.FootprintWidth * Game1.tileSize;
            float footprintPixelHeight = target.FootprintHeight * Game1.tileSize;

            // Long-range targets (e.g. the Saloon bar's 35-tile range) can easily sit off-screen.
            bool offLeft = screenPos.X + footprintPixelWidth < 0;
            bool offRight = screenPos.X > Game1.viewport.Width;
            bool offTop = screenPos.Y + footprintPixelHeight < 0;
            bool offBottom = screenPos.Y > Game1.viewport.Height;

            // The arrow and label align to the sprite's real top instead of the footprint, which for
            // NPCs/tall Furniture extends above it.
            float centerX = screenPos.X + footprintPixelWidth / 2f;
            float spriteTopY = screenPos.Y - target.ExtraHeightAboveFootprintPixels;

            if (offLeft || offRight || offTop || offBottom)
            {
                DrawOffScreenIndicator(b, target, centerX, spriteTopY, offLeft, offRight, offTop, offBottom);
                return;
            }

            // The highlight square stays aligned to the actual tile footprint - it's meant to
            // outline the footprint itself, not the sprite. Drawn at its true position and left to
            // clip naturally at the screen edge like any other world object would, since it only
            // makes sense drawn exactly on the object's own tile(s).
            if (config.ShowTargetHighlight)
                DrawSquareOutline(b, screenPos, footprintPixelWidth, footprintPixelHeight, GetCachedColor(config.HighlightColor));

            // The arrow and label sit further above the footprint still (ExtraHeightAboveFootprint
            // Pixels, plus their own gaps) - clamp just their anchor to stay on screen even when the
            // footprint itself is only partially visible near an edge (most commonly the top).
            // Otherwise a player relying on just the arrow/label (highlight square off) could have
            // something targeted with no visible cue at all. Unlike the fully-off-screen case, this
            // doesn't force them to show when their own settings are off, and doesn't rotate the
            // arrow - the target is still basically right here, just needs nudging into view.
            float minSpriteTopY = EdgeMargin + Game1.tileSize * 1.5f;
            float clampedCenterX = MathHelper.Clamp(centerX, EdgeMargin, Game1.viewport.Width - EdgeMargin);
            float clampedSpriteTopY = MathHelper.Clamp(spriteTopY, minSpriteTopY, Game1.viewport.Height - EdgeMargin);

            if (config.ShowTargetArrow)
            {
                const float gapAboveTile = 4f;
                Vector2 arrowCenter = new Vector2(clampedCenterX, clampedSpriteTopY - gapAboveTile - ArrowIconSize / 2f);
                DrawArrow(b, arrowCenter, rotation: 0f);
            }

            if (config.ShowTargetName && !string.IsNullOrEmpty(target.DisplayName))
            {
                // Without the arrow there's nothing to leave room for, so the label can sit much
                // closer to the sprite.
                float labelOffset = config.ShowTargetArrow ? Game1.tileSize * 1.1f : Game1.tileSize * 0.3f;
                Vector2 textPos = new Vector2(clampedCenterX, clampedSpriteTopY - labelOffset);
                Vector2 size = Game1.smallFont.MeasureString(target.DisplayName);
                textPos.X -= size.X / 2f;
                Utility.drawTextWithShadow(b, target.DisplayName, Game1.smallFont, textPos, Color.White);
            }
        }

        // When the target itself is off-screen, no highlight square is shown at all (there's no
        // footprint tile visible to outline) - instead the arrow and label ALWAYS show, regardless
        // of the ShowTargetArrow/ShowTargetName settings, pinned to the screen edge closest to the
        // target and rotated to point the rest of the way toward it.
        //
        // Deliberately axis-aligned rather than a straight ray from screen-center: only the axis (or
        // axes) the target is actually off-screen on get clamped to the edge, so e.g. a target that's
        // off-screen to the north stays at its own true, tile-accurate X position along the top edge
        // instead of drifting toward whatever a center-to-target ray would cross. A target that's off
        // on both axes (a corner case) gets a diagonal arrow pointing at that corner.
        private static void DrawOffScreenIndicator(SpriteBatch b, InteractionTarget target, float centerX, float spriteTopY,
            bool offLeft, bool offRight, bool offTop, bool offBottom)
        {
            float clampedX = offLeft ? EdgeMargin : offRight ? Game1.viewport.Width - EdgeMargin : centerX;
            float clampedY = offTop ? EdgeMargin : offBottom ? Game1.viewport.Height - EdgeMargin : spriteTopY;
            clampedX = MathHelper.Clamp(clampedX, EdgeMargin, Game1.viewport.Width - EdgeMargin);
            clampedY = MathHelper.Clamp(clampedY, EdgeMargin, Game1.viewport.Height - EdgeMargin);

            int dx = offLeft ? -1 : offRight ? 1 : 0;
            int dy = offTop ? -1 : offBottom ? 1 : 0;

            // The icon's un-rotated art points straight down (see DrawArrow) - rotating it to point
            // along (dx, dy) instead needs a -90-degree correction, since atan2(dy, dx) measures the
            // angle from "pointing right", not from "pointing down".
            float rotation = (float)Math.Atan2(dy, dx) - MathHelper.PiOver2;
            Vector2 arrowCenter = new Vector2(clampedX, clampedY);
            DrawArrow(b, arrowCenter, rotation);

            if (string.IsNullOrEmpty(target.DisplayName))
                return;

            const float labelGap = 26f;
            Vector2 size = Game1.smallFont.MeasureString(target.DisplayName);
            Vector2 textPos;

            if (dx != 0 && dy == 0)
            {
                // Off to the east/west only - label sits on the inward side of the arrow.
                textPos = dx > 0
                    ? new Vector2(clampedX - labelGap - size.X, clampedY - size.Y / 2f)
                    : new Vector2(clampedX + labelGap, clampedY - size.Y / 2f);
            }
            else
            {
                // Off to the north/south, or diagonally - label sits above/below the arrow.
                textPos = dy > 0
                    ? new Vector2(clampedX - size.X / 2f, clampedY - labelGap - size.Y)
                    : new Vector2(clampedX - size.X / 2f, clampedY + labelGap);
            }

            Utility.drawTextWithShadow(b, target.DisplayName, Game1.smallFont, textPos, Color.White);
        }

        // Four thin rectangles instead of a proper outline primitive - SpriteBatch doesn't have one.
        private static void DrawSquareOutline(SpriteBatch b, Vector2 topLeft, float width, float height, Color color)
        {
            int thickness = 4;
            Rectangle top = new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)width, thickness);
            Rectangle bottom = new Rectangle((int)topLeft.X, (int)(topLeft.Y + height - thickness), (int)width, thickness);
            Rectangle left = new Rectangle((int)topLeft.X, (int)topLeft.Y, thickness, (int)height);
            Rectangle right = new Rectangle((int)(topLeft.X + width - thickness), (int)topLeft.Y, thickness, (int)height);

            // Game1.staminaRect is a handy pre-loaded 1x1 white pixel, stretched to fill each rect.
            b.Draw(Game1.staminaRect, top, color);
            b.Draw(Game1.staminaRect, bottom, color);
            b.Draw(Game1.staminaRect, left, color);
            b.Draw(Game1.staminaRect, right, color);
        }

        private const int ArrowFrameSize = 9;
        private const float ArrowScale = 4f;
        private const float ArrowIconSize = ArrowFrameSize * ArrowScale;

        // Reuses the game's own "more dialogue below" icon (see DialogueBox.setUpNextPageIcon) - same
        // 6-frame animation, same texture, same timing. Its un-rotated art points straight down;
        // rotation rotates it clockwise from there (0 = still pointing down, unchanged from before).
        private static void DrawArrow(SpriteBatch b, Vector2 center, float rotation)
        {
            const int frameX = 232, frameY = 346, frameCount = 6;
            const float millisecondsPerFrame = 90f;

            double totalMilliseconds = Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0;
            int frame = (int)(totalMilliseconds % (frameCount * millisecondsPerFrame) / millisecondsPerFrame);
            Rectangle sourceRect = new Rectangle(frameX + frame * ArrowFrameSize, frameY, ArrowFrameSize, ArrowFrameSize);

            // Origin = icon's own center, so "center" is where the icon's middle lands, and rotation
            // spins it in place rather than around a corner.
            Vector2 origin = new Vector2(ArrowFrameSize / 2f, ArrowFrameSize / 2f);

            b.Draw(Game1.mouseCursors, center, sourceRect, Color.White, rotation, origin, ArrowScale, SpriteEffects.None, 1f);
        }

        // Caches the last-parsed hex string and its Color, so a fixed config value (the common case)
        // isn't re-parsed on every single draw call (up to 60x/sec while something's highlighted).
        private static string _lastParsedHex;
        private static Color _lastParsedColor = Color.Yellow;

        private static Color GetCachedColor(string hex)
        {
            if (hex != _lastParsedHex)
            {
                _lastParsedHex = hex;
                _lastParsedColor = ParseColor(hex);
            }
            return _lastParsedColor;
        }

        private static Color ParseColor(string hex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex))
                    return Color.Yellow;
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                {
                    int r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                    int g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                    int bch = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                    return new Color(r, g, bch);
                }
            }
            catch
            {
                // Bad config value - fall back to yellow rather than crash.
            }
            return Color.Yellow;
        }
    }
}
