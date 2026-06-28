namespace WorldZones.Mod.RegionOverlay.Overlay
{
    /// <summary>
    /// The region-overlay style dial, cycled by one hotkey
    /// (<c>Vanilla → Borders → BordersTint → Parchment → Atlas → Vanilla</c>). The resting default is
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

        /// <summary>
        /// The validated ATLAS composite (docs/design/region-atlas-render.md): biome-tinted region fill
        /// (low alpha, terrain reads through) + a depth-gated seaward coast glow + TERRESTRIAL-ONLY ink
        /// (interior land↔land borders only; coasts carried by the glow, not the ink). The coast halo is
        /// implied ON by this style regardless of the F7 dial. Gated + additive: the other stops are
        /// unchanged, so flipping away from Atlas restores the proven look.
        /// </summary>
        Atlas = 4,
    }

    /// <summary>Per-style layer flags derived from the locked draw table.</summary>
    public static class RegionOverlayStyleExtensions
    {
        /// <summary>True if this style draws the mesh ink seams.</summary>
        public static bool DrawsInk(this RegionOverlayStyle s) => s != RegionOverlayStyle.Vanilla;

        /// <summary>True if this style draws the baked area fill.</summary>
        public static bool DrawsFill(this RegionOverlayStyle s) =>
            s == RegionOverlayStyle.BordersTint || s == RegionOverlayStyle.Parchment
            || s == RegionOverlayStyle.Atlas;

        /// <summary>True if the fill should use the BIOME palette (Atlas) rather than the colourblind
        /// lightness ramp (BordersTint / Parchment).</summary>
        public static bool UsesBiomeFill(this RegionOverlayStyle s) => s == RegionOverlayStyle.Atlas;

        /// <summary>True if the ink should be filtered to TERRESTRIAL (interior land↔land) seams only —
        /// coastlines are left to the glow. Atlas only.</summary>
        public static bool TerrestrialInkOnly(this RegionOverlayStyle s) => s == RegionOverlayStyle.Atlas;

        /// <summary>True if this style implies the coast halo is ON regardless of the F7 dial (Atlas).</summary>
        public static bool ImpliesHalo(this RegionOverlayStyle s) => s == RegionOverlayStyle.Atlas;

        /// <summary>
        /// F8 toggle: <see cref="RegionOverlayStyle.Atlas"/> ⇄ <see cref="RegionOverlayStyle.Vanilla"/> (off).
        /// Decided 2026-06-28 (Daniel): Atlas is THE mode; Borders/BordersTint/Parchment are cut from the
        /// reachable cycle (the vector-ink line modes ride a zoom-registration drift Atlas doesn't). The
        /// enum members + their draw branches still exist internally pending the deeper cleanup ("nuance
        /// later") — this just makes F8 a clean Atlas/off toggle so the buggy modes can't be reached.
        /// Any non-Atlas state (incl. a legacy Borders value) toggles UP to Atlas; Atlas toggles to off.
        /// </summary>
        public static RegionOverlayStyle Next(this RegionOverlayStyle s) =>
            s == RegionOverlayStyle.Atlas ? RegionOverlayStyle.Vanilla : RegionOverlayStyle.Atlas;
    }
}
