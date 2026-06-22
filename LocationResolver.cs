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
            // (e.g. Redcrown Woods end=6 / Colossal Drain start=6), and that shared screen's
            // "unlock" is always tagged to the later area, i.e. the engine treats it as the
            // start of the next area rather than the tail of the previous one. Scanning from
            // the highest start downward picks that same area on such an overlap.
            for (int i = sortedLocations.Length - 1; i >= 0; i--)
            {
                Location location = sortedLocations[i];
                if (p_screenIndex1 >= location.start && p_screenIndex1 <= location.end)
                {
                    return new Result(location, true);
                }
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
