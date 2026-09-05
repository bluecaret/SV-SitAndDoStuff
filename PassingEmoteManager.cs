using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;
using SitAndDoStuff.Targeting;

namespace SitAndDoStuff
{
    // Makes nearby villagers/pets emote at the player while sitting - "happy" (32) for villagers,
    // "heart" (20) for pets. Character.doEmote() self-guards (no-ops mid-emote or during an event),
    // so this calls it freely with no extra checks.
    public class PassingEmoteManager
    {
        private const int VillagerEmote = 32; // "happy"
        private const int PetEmote = 20;      // "heart"

        // Cap on how many can emote on the very first check right after sitting down, so sitting
        // near a crowd doesn't make everyone emote in a pile at once.
        private const int MaxImmediateGreeters = 2;

        // Permanent for one sitting session - once greeted, a character won't emote again until
        // the player stands up and sits back down.
        private readonly HashSet<Character> _alreadyGreeted = new();

        // Used only to detect "just entered range" transitions from one tick to the next.
        private readonly HashSet<Character> _inRangeLastTick = new();

        private bool _isFirstCheck;

        // Called exactly once, the moment the player starts a new sitting session (not every
        // tick) - arms the "first check" special case.
        public void OnStartSitting()
        {
            _alreadyGreeted.Clear();
            _inRangeLastTick.Clear();
            _isFirstCheck = true;
        }

        // Called whenever sitting stops (or was never eligible to begin with).
        public void Reset()
        {
            _alreadyGreeted.Clear();
            _inRangeLastTick.Clear();
            _isFirstCheck = false;
        }

        public void Update(Farmer player, GameLocation location, ModConfig config)
        {
            if (!config.AllowPassingEmotes)
                return;

            Vector2 playerTile = player.Tile;
            var currentlyInRange = new List<(Character character, int emote)>();

            foreach (NPC npc in location.characters)
            {
                if (npc == null)
                    continue;

                int emote;
                CategorySettings settings;

                if (npc is Pet)
                {
                    emote = PetEmote;
                    settings = config.Pet;
                }
                else if (!npc.IsMonster && !(npc is Horse))
                {
                    // Same exclusions as the Talk/Gift NPC category - real villagers only.
                    emote = VillagerEmote;
                    settings = config.NPC;
                }
                else
                {
                    continue;
                }

                if (!TargetFinder.IsInRange(playerTile, npc.Tile, settings.Range, settings.MaxRange))
                    continue;

                currentlyInRange.Add((npc, emote));
            }

            var newlyInRange = currentlyInRange
                .Where(c => !_inRangeLastTick.Contains(c.character) && !_alreadyGreeted.Contains(c.character))
                .ToList();

            if (_isFirstCheck)
            {
                // Only the closest couple greet immediately; the rest aren't marked greeted, so
                // they'll still emote normally if they leave and re-enter range later.
                var closest = newlyInRange
                    .OrderBy(c => Vector2.DistanceSquared(playerTile, c.character.Tile))
                    .Take(MaxImmediateGreeters);

                foreach (var (character, emote) in closest)
                    Greet(character, emote);

                _isFirstCheck = false;
            }
            else
            {
                foreach (var (character, emote) in newlyInRange)
                    Greet(character, emote);
            }

            _inRangeLastTick.Clear();
            foreach (var (character, _) in currentlyInRange)
                _inRangeLastTick.Add(character);
        }

        private void Greet(Character character, int emote)
        {
            character.doEmote(emote);
            _alreadyGreeted.Add(character);
        }
    }
}