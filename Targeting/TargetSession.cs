using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;

namespace SitAndDoStuff.Targeting
{
    // Tracks cycling state for one continuous sitting stretch. Created once per session (via
    // TargetFinder.FindTargets) so ordering stays stable while cycling even if something moves.
    //
    // The first press after "None" lands on the target closest to the player's facing direction
    // ("F") and locks that press's direction as "forward" for the lap. Forward then walks through
    // every other target in turn; backward retraces toward F and back to "None" - so either button
    // alone can reach the entire ring over one full lap.
    public class TargetSession
    {
        public List<InteractionTarget> Targets { get; }
        public GameLocation Location { get; }

        private readonly float[] _angles; // AngleFromPlayer for each target, same order as Targets
        private readonly int _facingIndex; // index of the target closest to facing ("F")

        // 0..Targets.Count-1 = a real target, counted forward from F in _loopDirection.
        // Targets.Count = "None".
        private int _stepsFromF;
        private int _loopDirection = 1; // +1 (clockwise) or -1 (counter-clockwise); set on leaving "None"

        public bool HasSelection => _stepsFromF < Targets.Count;
        public InteractionTarget Current => HasSelection ? Targets[CurrentArrayIndex()] : null;

        public TargetSession(List<InteractionTarget> targets, GameLocation location, Vector2 playerTile, int facingDirection)
        {
            Location = location;

            // Absolute clockwise order from "up" - just the underlying array order used to find
            // "the next target over" in either direction; not itself the cycling order.
            targets.Sort((a, b) => AngleFromPlayer(a.Tile, playerTile).CompareTo(AngleFromPlayer(b.Tile, playerTile)));
            Targets = targets;

            _angles = new float[Targets.Count];
            for (int i = 0; i < Targets.Count; i++)
                _angles[i] = AngleFromPlayer(Targets[i].Tile, playerTile);

            float facingAngle = AngleFromPlayer(playerTile + TargetFinder.DirectionToVector(facingDirection), playerTile);
            _facingIndex = ClosestIndexToAngle(facingAngle, playerTile);

            _stepsFromF = Targets.Count; // start at "None"
        }

        public void MoveLeft() => Move(clockwise: false);
        public void MoveRight() => Move(clockwise: true);

        private void Move(bool clockwise)
        {
            if (Targets.Count == 0)
                return; // HasSelection stays false regardless; nothing to cycle to

            int pressDir = clockwise ? 1 : -1;

            if (_stepsFromF == Targets.Count)
            {
                // Leaving "None" - always land on F, and lock this press's direction as "forward"
                // for the lap. This runs identically whether it's a genuinely fresh sit-down or a
                // fresh lap after completing a previous one, which is exactly what makes either
                // button able to reach the whole ring on its own.
                _stepsFromF = 0;
                _loopDirection = pressDir;
                return;
            }

            // Pressing the direction that matches this lap's "forward" advances; pressing the
            // opposite one retraces back toward F (and eventually to "None").
            int delta = pressDir == _loopDirection ? 1 : -1;
            int totalSlots = Targets.Count + 1; // real targets + the "None" slot
            _stepsFromF = ((_stepsFromF + delta) % totalSlots + totalSlots) % totalSlots;
        }

        private int CurrentArrayIndex()
        {
            int n = Targets.Count;
            return ((_facingIndex + _loopDirection * _stepsFromF) % n + n) % n;
        }

        // Picks the target closest to "angle". When two or more targets sit at (essentially) the
        // same angle - e.g. stacked directly ahead of the player at different distances, which
        // easily happens on a tile grid - distance breaks the tie in favor of whichever is nearer,
        // rather than whichever happened to come first in Targets.
        private int ClosestIndexToAngle(float angle, Vector2 playerTile)
        {
            int bestIndex = 0;
            float bestDiff = float.MaxValue;
            float bestDistanceSq = float.MaxValue;
            const float angleTieEpsilon = 0.02f; // radians, ~1 degree

            for (int i = 0; i < Targets.Count; i++)
            {
                float diff = CircularDifference(_angles[i], angle);
                float distanceSq = Vector2.DistanceSquared(Targets[i].Tile, playerTile);

                if (diff < bestDiff - angleTieEpsilon)
                {
                    // Clearly better alignment - always wins outright.
                    bestDiff = diff;
                    bestDistanceSq = distanceSq;
                    bestIndex = i;
                }
                else if (diff < bestDiff + angleTieEpsilon && distanceSq < bestDistanceSq)
                {
                    // Roughly equally centered - prefer whichever is closer.
                    bestDiff = Math.Min(bestDiff, diff);
                    bestDistanceSq = distanceSq;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        // Angle from "up" (0,-1), increasing clockwise. atan2(dx, -dy) rather than the usual
        // atan2(dy, dx) because Stardew's Y axis points down, and we want 0 at "up" specifically.
        private static float AngleFromPlayer(Vector2 targetTile, Vector2 playerTile)
        {
            Vector2 delta = targetTile - playerTile;
            return (float)Math.Atan2(delta.X, -delta.Y);
        }

        private static float CircularDifference(float a, float b)
        {
            float twoPi = (float)(2 * Math.PI);
            float diff = Math.Abs(a - b) % twoPi;
            return diff > Math.PI ? twoPi - diff : diff;
        }
    }
}