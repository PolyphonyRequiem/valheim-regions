using System;

namespace WorldZones.WorldGen
{
    /// <summary>
    /// Lightweight 2D float vector replacing <c>UnityEngine.Vector2</c>.
    /// Provides the subset of operations used by WorldGenerator and related code.
    /// </summary>
    public struct Vector2 : IEquatable<Vector2>
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        /// <summary>Shorthand for <c>new Vector2(0, 0)</c>.</summary>
        public static readonly Vector2 zero = new Vector2(0f, 0f);

        /// <summary>Length of this vector.</summary>
        public float magnitude => (float)Math.Sqrt(x * x + y * y);

        /// <summary>Squared length of this vector.</summary>
        public float sqrMagnitude => x * x + y * y;

        /// <summary>Returns a unit-length copy of this vector.</summary>
        public Vector2 normalized
        {
            get
            {
                float m = magnitude;
                if (m < 1e-7f)
                    return zero;
                return new Vector2(x / m, y / m);
            }
        }

        /// <summary>Euclidean distance between two points.</summary>
        public static float Distance(Vector2 a, Vector2 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Squared magnitude of a vector (avoids sqrt).</summary>
        public static float SqrMagnitude(Vector2 a)
        {
            return a.x * a.x + a.y * a.y;
        }

        public static Vector2 operator +(Vector2 a, Vector2 b) =>
            new Vector2(a.x + b.x, a.y + b.y);

        public static Vector2 operator -(Vector2 a, Vector2 b) =>
            new Vector2(a.x - b.x, a.y - b.y);

        public static Vector2 operator -(Vector2 a) =>
            new Vector2(-a.x, -a.y);

        public static Vector2 operator *(Vector2 a, float d) =>
            new Vector2(a.x * d, a.y * d);

        public static Vector2 operator *(float d, Vector2 a) =>
            new Vector2(a.x * d, a.y * d);

        public static bool operator ==(Vector2 a, Vector2 b) =>
            a.x == b.x && a.y == b.y;

        public static bool operator !=(Vector2 a, Vector2 b) =>
            a.x != b.x || a.y != b.y;

        public bool Equals(Vector2 other) => x == other.x && y == other.y;
        public override bool Equals(object? obj) => obj is Vector2 v && Equals(v);
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 16);
        public override string ToString() => $"({x:F1}, {y:F1})";
    }
}
