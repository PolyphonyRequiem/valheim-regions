namespace WorldZones.Mod.RegionOverlay.Overlay
{
    /// <summary>
    /// The region-overlay style dial, cycled by one hotkey
    /// (<c>Vanilla → Borders → BordersTint → Parchment → Vanilla</c>). The resting default is
    /// <see cref="Borders"/> — the cheap useful version. Locked 2026-06-25 in
    /// docs/design/region-render-seam.md (<c>## Steps 2–3 lock</c>). Every stop is a TWEAK-ME
    /// starting dial; Daniel tunes stroke / tint / palette / the resting default in-world.
    /// </summary>
    public enum RegionOverlayStyle
    {
        /// <summary>No overlay — the untouched vanilla map (ink off, fill off).</summary>
        Vanilla = 0,

        /// <summary>Borders only — mesh ink seams, no fill. The resting default.</summary>
        Borders = 1,

        /// <summary>Borders + a translucent area fill over the vanilla terrain (terrain still reads).</summary>
        BordersTint = 2,

        /// <summary>Borders + an opaque area fill (terrain read replaced — the "parchment" territory view).</summary>
        Parchment = 3,
    }

    /// <summary>Per-style layer flags derived from the locked draw table.</summary>
    public static class RegionOverlayStyleExtensions
    {
        /// <summary>True if this style draws the mesh ink seams.</summary>
        public static bool DrawsInk(this RegionOverlayStyle s) => s != RegionOverlayStyle.Vanilla;

        /// <summary>True if this style draws the baked area fill.</summary>
        public static bool DrawsFill(this RegionOverlayStyle s) =>
            s == RegionOverlayStyle.BordersTint || s == RegionOverlayStyle.Parchment;

        /// <summary>Advance to the next style in the cycle order, wrapping to <see cref="RegionOverlayStyle.Vanilla"/>.</summary>
        public static RegionOverlayStyle Next(this RegionOverlayStyle s) =>
            s == RegionOverlayStyle.Parchment ? RegionOverlayStyle.Vanilla : (s + 1);
    }
}
