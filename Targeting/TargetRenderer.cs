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
        public static void Draw(SpriteBatch b, InteractionTarget target, ModConfig config)
        {
            if (target == null)
                return;

            Color color = ParseColor(config.HighlightColor);

            Vector2 worldPos = new Vector2(target.Tile.X * Game1.tileSize, target.Tile.Y * Game1.tileSize);
            Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos);

            float footprintPixelWidth = target.FootprintWidth * Game1.tileSize;
            float footprintPixelHeight = target.FootprintHeight * Game1.tileSize;

            // The highlight square stays aligned to the actual tile footprint - it's meant to
            // outline the footprint itself, not the sprite.
            if (config.ShowTargetHighlight)
                DrawSquareOutline(b, screenPos, footprintPixelWidth, footprintPixelHeight, color);

            // The arrow and label, on the other hand, align to the top of the actual SPRITE, which
            // for things like NPCs and tall Furniture (TVs) extends above the footprint - screenPos.Y
            // minus that extra height is the sprite's real top edge.
            float centerX = screenPos.X + footprintPixelWidth / 2f;
            float spriteTopY = screenPos.Y - target.ExtraHeightAboveFootprintPixels;

            if (config.ShowTargetArrow)
                DrawArrow(b, new Vector2(centerX, spriteTopY));

            if (config.ShowTargetName && !string.IsNullOrEmpty(target.DisplayName))
            {
                // Without the arrow there's nothing to leave room for, so the label can sit much
                // closer to the sprite.
                float labelOffset = config.ShowTargetArrow ? Game1.tileSize * 1.1f : Game1.tileSize * 0.3f;
                Vector2 textPos = new Vector2(centerX, spriteTopY - labelOffset);
                Vector2 size = Game1.smallFont.MeasureString(target.DisplayName);
                textPos.X -= size.X / 2f;
                Utility.drawTextWithShadow(b, target.DisplayName, Game1.smallFont, textPos, Color.White);
            }
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

        // Reuses the game's own "more dialogue below" icon (see DialogueBox.setUpNextPageIcon) -
        // same 6-frame animation, same texture, same timing.
        private static void DrawArrow(SpriteBatch b, Vector2 topCenter)
        {
            const int frameX = 232, frameY = 346, frameSize = 9, frameCount = 6;
            const float millisecondsPerFrame = 90f;
            const float scale = 4f;

            double totalMilliseconds = Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0;
            int frame = (int)(totalMilliseconds % (frameCount * millisecondsPerFrame) / millisecondsPerFrame);
            Rectangle sourceRect = new Rectangle(frameX + frame * frameSize, frameY, frameSize, frameSize);

            // Origin = icon's own center, so "position" below means "where the center should be"
            // rather than the sprite's top-left corner.
            Vector2 origin = new Vector2(frameSize / 2f, frameSize / 2f);

            float iconHeight = frameSize * scale;
            const float gapAboveTile = 4f;
            Vector2 position = new Vector2(topCenter.X, topCenter.Y - gapAboveTile - iconHeight / 2f);

            b.Draw(Game1.mouseCursors, position, sourceRect, Color.White, 0f, origin, scale, SpriteEffects.None, 1f);
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
