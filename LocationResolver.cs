using JumpKing.MiscSystems.LocationText;

namespace AreaInfoDisplayOnPause
{
    internal static class LocationResolver
    {
        public readonly struct Result
        {
            public Result(Location? area, bool isExactMatch)
            {
                Area = area;
                IsExactMatch = isExactMatch;
            }

            public Location? Area { get; }
            public bool IsExactMatch { get; }
        }

        /// <summary>
        /// sortedLocations must be sorted ascending by start. Returns the Location containing
        /// p_screenIndex1 (pattern A), or failing that the closest preceding Location whose
        /// start is at or before p_screenIndex1 (pattern B, the "gap" between two areas), or
        /// no Location at all if p_screenIndex1 is before every defined area.
        /// </summary>
        public static Result Resolve(Location[] sortedLocations, int p_screenIndex1)
        {
            // The vanilla data deliberately shares a boundary screen between consecutive areas
            // (e.g. Redcrown Woods end=6 / Colossal Drain start=6), but which of the two a
            // shared screen belongs to isn't fixed by "prefer the later start": it depends on
            // each area's own "unlock" field, which is exactly the screen number the engine
            // itself uses to flip its "new area discovered" state (see LocationComp.
            // CheckIfNewScreen, which advances only once p_screenIndex1 == unlock). Sometimes
            // unlock == start (Colossal Drain: start=6, unlock=6 -> wins screen 6 outright), but
            // often unlock is start+1 or more (False Kings Keep: start=10, unlock=11 -> screen 10
            // still belongs to the previous area, Colossal Drain). So among overlapping
            // candidates, only those that have already "unlocked" by this screen are eligible,
            // and among those, the one with the latest start wins.
            Location? best = null;
            for (int i = 0; i < sortedLocations.Length; i++)
            {
                Location location = sortedLocations[i];
                if (p_screenIndex1 >= location.start && p_screenIndex1 <= location.end && p_screenIndex1 >= location.unlock
                    && (!best.HasValue || location.start > best.Value.start))
                {
                    best = location;
                }
            }
            if (best.HasValue)
            {
                return new Result(best, true);
            }
            for (int i = sortedLocations.Length - 1; i >= 0; i--)
            {
                if (sortedLocations[i].start <= p_screenIndex1)
                {
                    return new Result(sortedLocations[i], false);
                }
            }
            return new Result(null, false);
        }
    }
}
