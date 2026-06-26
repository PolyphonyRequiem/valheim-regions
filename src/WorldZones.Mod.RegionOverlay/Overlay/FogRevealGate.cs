using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WorldZones.Mod.RegionOverlay.Overlay
{
    /// <summary>
    /// The per-pixel fog-of-war gate for the region overlay. The regions reveal fog-respectingly: a
    /// seam or fill texel draws ONLY where the player has explored. The real reveal mask is vanilla
    /// <c>Minimap.m_explored[]</c> via the PRIVATE <c>bool Minimap.IsExplored(Vector3)</c>
    /// (Minimap.cs:1620, decomp-verified private) — NOT the mod's <c>IsHoverExplored</c> (that is a
    /// single-point hover-LABEL check on <c>m_biomeNameLarge.text</c>, not an area mask).
    ///
    /// <para>So this resolves a cached reflected handle to <c>IsExplored</c> — the SAME pattern
    /// <c>RegionOverlayPlugin.BuildRiverResolver</c> uses for the private
    /// <c>WorldGenerator.GetRiverWeight</c>. If the handle can't be resolved (a future version rename),
    /// it degrades to <see cref="IsExplored"/> returning <c>false</c> for EVERY point — i.e. the
    /// overlay draws NOTHING, never the whole unfogged map (spoiling the world is the exact failure the
    /// fog mock rejected). AC-T3-FOG-1/2.</para>
    /// </summary>
    public sealed class FogRevealGate
    {
        private readonly ManualLogSource? logger;
        private readonly MethodInfo? isExplored;
        private object[]? argBuffer;

        /// <summary>True iff the reflected <c>IsExplored</c> handle resolved; when false the overlay
        /// must disable itself (fail-closed: no unfogged fallback draw).</summary>
        public bool Available => this.isExplored != null;

        public FogRevealGate(ManualLogSource? logger)
        {
            this.logger = logger;

            // bool Minimap.IsExplored(Vector3) — private instance method.
            Type minimapType = AccessTools.TypeByName("Minimap");
            this.isExplored = minimapType == null
                ? null
                : AccessTools.Method(minimapType, "IsExplored", new[] { typeof(Vector3) });

            if (this.isExplored == null)
            {
                this.logger?.LogWarning(
                    "Minimap.IsExplored(Vector3) not found via reflection — the region overlay will be " +
                    "DISABLED (no fog mask). Drawing the whole unfogged map is refused by design (it spoils " +
                    "exploration). Likely a Valheim version signature change.");
            }
            else
            {
                this.argBuffer = new object[1];
            }
        }

        /// <summary>
        /// True if <paramref name="worldPoint"/> is inside the player's explored fog. Fails CLOSED:
        /// returns false (not explored → don't draw) if the handle is unavailable or the reflected
        /// call throws.
        /// </summary>
        public bool IsExplored(global::Minimap minimap, Vector3 worldPoint)
        {
            if (this.isExplored == null || minimap == null || this.argBuffer == null) return false;
            try
            {
                this.argBuffer[0] = worldPoint;
                object result = this.isExplored.Invoke(minimap, this.argBuffer);
                return result is bool b && b;
            }
            catch (Exception ex)
            {
                this.logger?.LogWarning($"Minimap.IsExplored reflected call failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>World XZ convenience: probes at Y=0 (the map fog is XZ-indexed; Y is ignored).</summary>
        public bool IsExplored(global::Minimap minimap, double worldX, double worldZ)
            => IsExplored(minimap, new Vector3((float)worldX, 0f, (float)worldZ));
    }
}
