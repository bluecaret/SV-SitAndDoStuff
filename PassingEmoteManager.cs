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

        // How long after triggering a greet actually shows the emote - feels more like a natural
        // reaction/delay instead of firing the instant the character enters range.
        private const double GreetDelayMs = 1000;

        // Permanent for one sitting session - once greeted, a character won't emote again until
        // the player stands up and sits back down. Marked at trigger time (not once the delayed
        // emote actually fires), so a character can't queue up a second greet while the first is
        // still pending.
        private readonly HashSet<Character> _alreadyGreeted = new();

        // Used only to detect "just entered range" transitions from one tick to the next.
        private readonly HashSet<Character> _inRangeLastTick = new();

        // Characters that have been triggered but whose emote hasn't fired yet, with remaining
        // delay (ms) and which emote to play once it reaches zero.
        private readonly Dictionary<Character, (double remainingMs, int emote)> _pendingGreets = new();

        private bool _isFirstCheck;

        // Called exactly once, the moment the player starts a new sitting session (not every
        // tick) - arms the "first check" special case.
        public void OnStartSitting()
        {
            _alreadyGreeted.Clear();
            _inRangeLastTick.Clear();
            _pendingGreets.Clear();
            _isFirstCheck = true;
        }

        // Called whenever sitting stops (or was never eligible to begin with).
        public void Reset()
        {
            _alreadyGreeted.Clear();
            _inRangeLastTick.Clear();
            _pendingGreets.Clear();
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
                    ScheduleGreet(character, emote);

                _isFirstCheck = false;
            }
            else
            {
                foreach (var (character, emote) in newlyInRange)
                    ScheduleGreet(character, emote);
            }

            _inRangeLastTick.Clear();
            foreach (var (character, _) in currentlyInRange)
                _inRangeLastTick.Add(character);

            AdvancePendingGreets(currentlyInRange);
        }

        private void ScheduleGreet(Character character, int emote)
        {
            _pendingGreets[character] = (GreetDelayMs, emote);
            _alreadyGreeted.Add(character);
        }

        private void AdvancePendingGreets(List<(Character character, int emote)> currentlyInRange)
        {
            if (_pendingGreets.Count == 0)
                return;

            double elapsedMs = Game1.currentGameTime?.ElapsedGameTime.TotalMilliseconds ?? 0;
            List<Character> fired = null;

            foreach (Character character in _pendingGreets.Keys.ToList())
            {
                (double remainingMs, int emote) = _pendingGreets[character];
                remainingMs -= elapsedMs;
                if (remainingMs > 0)
                {
                    _pendingGreets[character] = (remainingMs, emote);
                    continue;
                }

                (fired ??= new List<Character>()).Add(character);

                // Only actually emote if the character's still around to be seen doing it - if they
                // wandered off mid-delay, there's nothing left to greet.
                if (currentlyInRange.Any(c => c.character == character))
                    character.doEmote(emote);
            }

            if (fired != null)
                foreach (Character character in fired)
                    _pendingGreets.Remove(character);
        }
    }
}