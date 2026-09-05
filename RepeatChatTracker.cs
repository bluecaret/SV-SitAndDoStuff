using System.Collections.Generic;
using StardewValley;

namespace SitAndDoStuff
{
    // Tracks, per NPC, how many of the three repeat-chat "families" (see TargetFinder's NPC
    // section) have been tried this sitting session. In-memory only, reset on standing up - standing
    // and sitting back down lets the same chain repeat, an accepted trade-off.
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