namespace YtProducer.Media.Services;

public static class MathUtils
{
    public static int NextPowerOfTwo(int value)
    {
        if (value <= 1)
        {
            return 1;
        }

        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return value + 1;
    }

    public static float Repeat01(float value)
    {
        value %= 1f;
        if (value < 0)
        {
            value += 1f;
        }

        return value;
    }

    public static float HashToUnit(int seed, int a, int b)
    {
        unchecked
        {
            uint x = (uint)seed;
            x ^= 0x9E3779B9u;
            x += (uint)a * 0x85EBCA6Bu;
            x ^= x >> 13;
            x += (uint)b * 0xC2B2AE35u;
            x ^= x >> 16;
            return (x & 0x00FFFFFF) / 16777216f;
        }
    }

    public static float HashToSignedUnit(int seed, int a, int b)
    {
        return HashToUnit(seed, a, b) * 2f - 1f;
    }
}
