namespace KeyValueStore.Server;

/// <summary>Simple glob matching supporting * and ? (used by KEYS and PSUBSCRIBE).</summary>
internal static class Glob
{
    public static bool Match(ReadOnlySpan<char> input, ReadOnlySpan<char> pattern)
    {
        int i = 0, p = 0;
        int starIdx = -1, matchIdx = 0;

        while (i < input.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                starIdx = p;
                matchIdx = i;
                p++;
            }
            else if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == input[i]))
            {
                i++;
                p++;
            }
            else if (starIdx != -1)
            {
                p = starIdx + 1;
                matchIdx++;
                i = matchIdx;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
            p++;

        return p == pattern.Length;
    }
}
