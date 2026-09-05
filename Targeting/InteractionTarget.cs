using System;
using Microsoft.Xna.Framework;
using StardewValley;

namespace SitAndDoStuff.Targeting
{
    // One candidate the player could interact with right now: what it is, where it is, its label,
    // and the invoke delegate that actually performs the interaction (set by TargetFinder). The
    // delegate is what lets ModEntry call target.Invoke(player) without caring whether it's a TV,
    // an NPC, or the Saloon bar.
    public class InteractionTarget
    {
        public InteractionCategory Category { get; }

        // Top-left tile of the target's footprint. Used for range/facing checks and for positioning
        // the highlight.
        public Vector2 Tile { get; }

        // Footprint size in tiles. 1x1 for almost everything; TVs and other multi-tile Furniture set
        // this higher so the highlight and range checks account for the whole sprite, not just Tile.
        public int FootprintWidth { get; }
        public int FootprintHeight { get; }

        // How far the visual sprite extends above the footprint's top edge, in pixels - lets the
        // arrow/label align to a tall sprite's real top (NPCs, TVs) instead of cutting through it.
        // 0 for anything that renders flush with its footprint.
        public float ExtraHeightAboveFootprintPixels { get; }

        public string DisplayName { get; }

        private readonly Action<Farmer> _invoke;

        public InteractionTarget(InteractionCategory category, Vector2 tile, string displayName, Action<Farmer> invoke,
            int footprintWidth = 1, int footprintHeight = 1, float extraHeightAboveFootprintPixels = 0f)
        {
            Category = category;
            Tile = tile;
            FootprintWidth = Math.Max(1, footprintWidth);
            FootprintHeight = Math.Max(1, footprintHeight);
            ExtraHeightAboveFootprintPixels = Math.Max(0f, extraHeightAboveFootprintPixels);
            DisplayName = displayName;
            _invoke = invoke;
        }

        public void Invoke(Farmer who) => _invoke?.Invoke(who);
    }
}
