using System;

namespace WorldZones.WorldGen
{
    public struct Vector2i : IEquatable<Vector2i>
    {
        public int x;
        public int y;

        public Vector2i(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public bool Equals(Vector2i other)
        {
            return x == other.x && y == other.y;
        }

        public override bool Equals(object obj)
        {
            return obj is Vector2i other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (x * 397) ^ y;
        }

        public static bool operator ==(Vector2i left, Vector2i right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Vector2i left, Vector2i right)
        {
            return !left.Equals(right);
        }
    }
}
