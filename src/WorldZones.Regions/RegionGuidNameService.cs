using System;
namespace WorldZones.Regions
{
    public static class RegionGuidNameService
    {
        public static string CreateDeterministicName(string worldId, int regionId)
        {
            if (string.IsNullOrWhiteSpace(worldId))
            {
                throw new ArgumentException("worldId must not be null or empty", nameof(worldId));
            }

            if (regionId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(regionId), "regionId must be >= 0");
            }

            unchecked
            {
                int worldHash = GetStableHashCode(worldId);
                uint mixed = MixRegionKey(worldHash, regionId);
                int index = (int)(mixed % (uint)RegionNameCatalog.Count);
                return RegionNameCatalog.GetByIndex(index);
            }
        }

        private static uint MixRegionKey(int worldHash, int regionId)
        {
            uint value = (uint)regionId * 0x9E3779B1u;
            value ^= (uint)worldHash;

            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;

            return value;
        }

        private static int GetStableHashCode(string value)
        {
            int hash = 5381;
            int hash2 = hash;

            for (int i = 0; i < value.Length && value[i] != '\0'; i += 2)
            {
                hash = ((hash << 5) + hash) ^ value[i];
                if (i == value.Length - 1 || value[i + 1] == '\0')
                {
                    break;
                }

                hash2 = ((hash2 << 5) + hash2) ^ value[i + 1];
            }

            return hash + (hash2 * 1566083941);
        }
    }
}
