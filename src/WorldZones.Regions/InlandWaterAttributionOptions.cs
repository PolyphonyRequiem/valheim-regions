namespace WorldZones.Regions
{
    /// <summary>
    /// Controls optional inland-water attribution behavior after land proto-region generation.
    /// </summary>
    public sealed class InlandWaterAttributionOptions
    {
        /// <summary>
        /// Gets default options with inland-water attribution disabled.
        /// </summary>
        public static InlandWaterAttributionOptions Disabled { get; } = new InlandWaterAttributionOptions();

        /// <summary>
        /// When true, inland-water bodies are attributed to adjacent regions.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Creates a validated copy of this options object.
        /// </summary>
        public InlandWaterAttributionOptions Validated()
        {
            return new InlandWaterAttributionOptions
            {
                Enabled = this.Enabled
            };
        }
    }
}
