using System.Collections.Generic;
using StardewValley;

namespace SitAndDoStuff
{
    // Tracks, per NPC, how many of the three repeat-chat "families" (see TargetFinder's NPC
    // section) have already been tried this sitting session. Deliberately in-memory only and
    // reset on standing up, rather than tracked per-day - standing up and sitting back down lets
    // the same short chain repeat, which is an accepted, simple trade-off rather than a bug.
    public class RepeatChatTracker
    {
        private const int FamilyCount = 3;

        private readonly Dictionary<NPC, int> _familyIndexByNpc = new();

        public void Reset() => _familyIndexByNpc.Clear();

        // Returns the next family index to try for this NPC (0, 1, 2, ...), advancing it for next
        // time. Returns -1 once every family has already been tried this sitting session.
        public int GetNextFamilyIndex(NPC npc)
        {
            _familyIndexByNpc.TryGetValue(npc, out int index);
            if (index >= FamilyCount)
                return -1;

            _familyIndexByNpc[npc] = index + 1;
            return index;
        }
    }
}