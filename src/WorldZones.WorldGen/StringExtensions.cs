namespace WorldZones.WorldGen
{
    /// <summary>
    /// Extension methods for string hashing.
    /// Implements Valheim's GetStableHashCode for deterministic seed hashing.
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        /// Gets a stable hash code for a string.
        /// Replicates Valheim's assembly_utils.GetStableHashCode implementation.
        /// Unlike string.GetHashCode(), this is guaranteed to be consistent across platforms and .NET versions.
        /// </summary>
        public static int GetStableHashCode(this string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return 0;
            }
            
            // Implementation matching Valheim's assembly_utils
            // Uses a simple but effective hash algorithm
            int hash = 5381;
            int hash2 = hash;
            
            for (int i = 0; i < str.Length && str[i] != '\0'; i += 2)
            {
                hash = ((hash << 5) + hash) ^ str[i];
                if (i == str.Length - 1 || str[i + 1] == '\0')
                {
                    break;
                }
                hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
            }
            
            return hash + (hash2 * 1566083941);
        }
    }
}
