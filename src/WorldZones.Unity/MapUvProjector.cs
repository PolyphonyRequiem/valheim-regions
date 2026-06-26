using UnityEngine;
using WorldZones.Runtime.Geometry;

namespace WorldZones.Unity
{
    /// <summary>
    /// Unity <see cref="Vector2"/>/<see cref="Vector3"/> wrapper over the committed Tier-1
    /// <see cref="MapProjector"/> + <see cref="MapFrame"/>. PURE TYPE ADAPTATION — zero new math: the
    /// projection arithmetic lives in Tier-1 (under the net8 headless test net), this only converts
    /// Unity vectors ↔ the Tier-1 <see cref="WzVec2"/>/<see cref="MapUv"/> currency. See
    /// docs/design/region-render-seam.md (AC-T2-PROJ-1/2).
    /// </summary>
    public static class MapUvProjector
    {
        /// <summary>
        /// World metres (the XZ plane of a Unity <see cref="Vector3"/> — Y is up and ignored) →
        /// normalised map UV as a Unity <see cref="Vector2"/>. Delegates to
        /// <see cref="MapProjector.Project"/>.
        /// </summary>
        public static Vector2 Project(Vector3 world, MapFrame frame)
            => ToVector2(MapProjector.Project(new WzVec2(world.x, world.z), frame));

        /// <summary>
        /// World metres as a Unity <see cref="Vector2"/> (x = world-X, y = world-Z) → normalised map
        /// UV. Delegates to <see cref="MapProjector.Project"/>.
        /// </summary>
        public static Vector2 Project(Vector2 worldXZ, MapFrame frame)
            => ToVector2(MapProjector.Project(new WzVec2(worldXZ.x, worldXZ.y), frame));

        /// <summary>
        /// Normalised map UV → world XZ (x = world-X, y = world-Z), the inverse of
        /// <see cref="Project(Vector2,MapFrame)"/>. Delegates to <see cref="MapProjector.Unproject"/> —
        /// the same transform the mod's <c>TryGetHoverWorldPosition</c> hand-rolled before Tier-1.
        /// </summary>
        public static Vector2 Unproject(Vector2 uv, MapFrame frame)
        {
            WzVec2 world = MapProjector.Unproject(new MapUv(uv.x, uv.y), frame);
            return new Vector2((float)world.X, (float)world.Z);
        }

        private static Vector2 ToVector2(MapUv uv) => new Vector2((float)uv.U, (float)uv.V);
    }
}
