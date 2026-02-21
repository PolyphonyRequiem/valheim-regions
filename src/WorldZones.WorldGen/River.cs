namespace WorldZones.WorldGen
{
    public class River
    {
        public UnityEngine.Vector2 p0;
        public UnityEngine.Vector2 p1;
        public UnityEngine.Vector2 center;
        public float widthMin;
        public float widthMax;
        public float curveWidth;
        public float curveWavelength;
    }

    public struct RiverPoint
    {
        public UnityEngine.Vector2 p;
        public float w;
        public float w2;

        public RiverPoint(UnityEngine.Vector2 p, float w)
        {
            this.p = p;
            this.w = w;
            this.w2 = w * w;
        }
    }
}
