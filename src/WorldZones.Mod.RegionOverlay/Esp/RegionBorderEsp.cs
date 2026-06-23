using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldZones.Mod.RegionOverlay.Esp
{
    /// <summary>
    /// 🔴 STATUS: SCAFFOLD — compile-unverified on Linux (needs the Windows CLIENT rig + Valheim/Unity
    /// assemblies via $(ValheimModdedPath); see valheim-worldzones-development skill, client-vs-server
    /// build constraint). The region/border MATH it consumes is verified; the Unity RENDERING below is
    /// written against the documented API but NOT compiled or walked in-world yet. Do not claim it works
    /// until built against a real client and seen on the ground.
    ///
    /// The "regions ESP": draws region BORDERS as ground-projected world-space line segments you can
    /// walk up to — the decision instrument for the border-rewrite backtrack. You can't judge a border
    /// from a top-down PNG; you have to see it where you stand. (docs/design/region-borders.md: "What's
    /// still gated on the ESP".)
    ///
    /// Design:
    ///  - Sample the region grid in a window around the player (cheap: data already in memory).
    ///  - A border exists between two 64m zones with different owners. Emit the shared zone-edge as a
    ///    world-space segment, projected onto terrain height (GetHeight) at several points so it hugs
    ///    the ground rather than floating.
    ///  - Toggle with a hotkey. Pool LineRenderers; refresh only when the player crosses a zone.
    ///  - This draws the CLASSIFICATION border (64m zone edges). Sub-zone contour-hugging (the v3 cost
    ///    field result) is a later layer — first see whether the 64m staircase even reads as wrong.
    /// </summary>
    public sealed class RegionBorderEsp : MonoBehaviour
    {
        // ── Tunables ──────────────────────────────────────────────
        public KeyCode ToggleKey = KeyCode.F9;
        public float WindowRadiusMeters = 256f;   // how far around the player to draw borders
        public float LineWidth = 0.35f;
        public float HeightOffset = 0.3f;         // lift slightly so the line isn't z-fought by ground
        public int SegmentSubdivisions = 8;       // points per 64m edge to follow terrain undulation
        public Color BorderColor = new Color(1f, 0.85f, 0.2f, 0.85f);

        // ── Injected dependencies (set by the host plugin) ────────
        // A function (worldX, worldZ) -> region id, or int.MinValue if none. Backed by the verified
        // RegionLookupService grid already computed at world load.
        public Func<float, float, int> RegionIdAt;
        // Terrain height sampler — global::WorldGenerator.instance.GetHeight in-game.
        public Func<float, float, float> HeightAt;

        private const float ZoneSize = 64f; // matches ZoneSystem.c_ZoneSize / ZoneGrid.ZoneSize

        private bool visible;
        private readonly List<LineRenderer> pool = new List<LineRenderer>();
        private int usedThisFrame;
        private Vector2Int lastPlayerZone = new Vector2Int(int.MinValue, int.MinValue);
        private Material lineMaterial;

        private void Awake()
        {
            // Unlit vertex-colored material so the line is visible without scene lighting.
            this.lineMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        private void Update()
        {
            if (Input.GetKeyDown(this.ToggleKey))
            {
                this.visible = !this.visible;
                if (!this.visible) this.HideAll();
            }
            if (!this.visible) return;
            if (this.RegionIdAt == null || this.HeightAt == null) return;

            var player = Player.m_localPlayer;
            if (player == null) return;
            Vector3 p = player.transform.position;

            // Only rebuild when the player crosses into a new 64m zone (cheap; borders don't move).
            var zone = new Vector2Int(Mathf.FloorToInt((p.x + ZoneSize / 2f) / ZoneSize),
                                      Mathf.FloorToInt((p.z + ZoneSize / 2f) / ZoneSize));
            if (zone != this.lastPlayerZone)
            {
                this.lastPlayerZone = zone;
                this.Rebuild(p);
            }
        }

        private void Rebuild(Vector3 center)
        {
            this.usedThisFrame = 0;
            int zr = Mathf.CeilToInt(this.WindowRadiusMeters / ZoneSize);

            for (int dz = -zr; dz <= zr; dz++)
            {
                for (int dx = -zr; dx <= zr; dx++)
                {
                    float cx = center.x + dx * ZoneSize;
                    float cz = center.z + dz * ZoneSize;
                    int here = this.RegionIdAt(cx, cz);
                    if (here == int.MinValue) continue;

                    // East neighbor edge (shared vertical edge at x = cx + 32)
                    int east = this.RegionIdAt(cx + ZoneSize, cz);
                    if (east != here && east != int.MinValue)
                        this.DrawEdge(cx + ZoneSize / 2f, cz - ZoneSize / 2f, cx + ZoneSize / 2f, cz + ZoneSize / 2f);

                    // North neighbor edge (shared horizontal edge at z = cz + 32)
                    int north = this.RegionIdAt(cx, cz + ZoneSize);
                    if (north != here && north != int.MinValue)
                        this.DrawEdge(cx - ZoneSize / 2f, cz + ZoneSize / 2f, cx + ZoneSize / 2f, cz + ZoneSize / 2f);
                }
            }
            // Hide any leftover pooled renderers from a denser previous frame.
            for (int i = this.usedThisFrame; i < this.pool.Count; i++)
                this.pool[i].enabled = false;
        }

        private void DrawEdge(float x0, float z0, float x1, float z1)
        {
            LineRenderer lr = this.GetPooled();
            lr.enabled = true;
            lr.positionCount = this.SegmentSubdivisions + 1;
            for (int s = 0; s <= this.SegmentSubdivisions; s++)
            {
                float t = (float)s / this.SegmentSubdivisions;
                float wx = Mathf.Lerp(x0, x1, t);
                float wz = Mathf.Lerp(z0, z1, t);
                float wy = this.HeightAt(wx, wz) + this.HeightOffset;
                lr.SetPosition(s, new Vector3(wx, wy, wz));
            }
        }

        private LineRenderer GetPooled()
        {
            if (this.usedThisFrame < this.pool.Count)
                return this.pool[this.usedThisFrame++];

            var go = new GameObject("WZ_EspBorderLine");
            go.transform.SetParent(this.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = this.lineMaterial;
            lr.widthMultiplier = this.LineWidth;
            lr.numCapVertices = 2;
            lr.startColor = lr.endColor = this.BorderColor;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            this.pool.Add(lr);
            this.usedThisFrame++;
            return lr;
        }

        private void HideAll()
        {
            foreach (var lr in this.pool) lr.enabled = false;
            this.usedThisFrame = 0;
            this.lastPlayerZone = new Vector2Int(int.MinValue, int.MinValue);
        }

        private void OnDestroy()
        {
            foreach (var lr in this.pool)
                if (lr != null) UnityEngine.Object.Destroy(lr.gameObject);
            this.pool.Clear();
        }
    }
}
