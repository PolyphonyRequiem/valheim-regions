using System;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// The pure world↔map projection. This is the math lifted out of the mod's
    /// <c>MinimapUpdateBiomePatch.TryGetHoverWorldPosition</c> (which read a live <c>Minimap</c>) and
    /// generalised to consume a <see cref="MapFrame"/> instead. Tier-2 (<c>WorldZones.Unity</c>)
    /// builds a frame from a live <c>Minimap</c> via <c>ValheimMapConventions</c>; this layer stays
    /// game-free and net8-testable so the projection math is under the headless regression net.
    /// See docs/design/region-render-seam.md.
    /// </summary>
    public static class MapProjector
    {
        /// <summary>World metres → normalised map UV under the given frame.</summary>
        public static MapUv Project(WzVec2 world, MapFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));

            double dx = world.X - frame.CenterX;
            double dz = world.Z - frame.CenterZ;

            // Rotate the world offset by -rotation so a frame rotation of +θ turns the rendered
            // content counter-clockwise (the conventional "heading-up" sense).
            double cos = Math.Cos(frame.RotationRadians);
            double sin = Math.Sin(frame.RotationRadians);
            double rx = dx * cos + dz * sin;
            double rz = -dx * sin + dz * cos;

            double u = 0.5 + rx / frame.SpanX;
            double v = 0.5 + rz / frame.SpanZ;
            return new MapUv(u, v);
        }

        /// <summary>
        /// Normalised map UV → world metres — the inverse of <see cref="Project"/>, and the original
        /// hover→world use case (cursor over the map → which world point / region).
        /// </summary>
        public static WzVec2 Unproject(MapUv uv, MapFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));

            double rx = (uv.U - 0.5) * frame.SpanX;
            double rz = (uv.V - 0.5) * frame.SpanZ;

            double cos = Math.Cos(frame.RotationRadians);
            double sin = Math.Sin(frame.RotationRadians);
            // Inverse rotation (R(+θ), the transpose of Project's R(-θ)).
            double dx = rx * cos - rz * sin;
            double dz = rx * sin + rz * cos;

            return new WzVec2(dx + frame.CenterX, dz + frame.CenterZ);
        }
    }
}
