using UnityEngine;

public static class UnityPerlinNoise
{
    public static float GetNoise(float x, float y)
    {
        return Mathf.PerlinNoise(x, y);
    }
}
