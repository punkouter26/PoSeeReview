using System.Buffers.Binary;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Vector packing and comparison for the comic similarity index.
/// </summary>
internal static class VectorMath
{
    /// <summary>
    /// Packs a vector for Table Storage, which persists primitives and has no array-of-float
    /// column. <c>byte[]</c> is a first-class Table type (<c>Edm.Binary</c>) and a 768-dimension
    /// vector is 3 KB — well inside the per-property limit and invisible next to the narrative.
    /// <para>
    /// Written little-endian explicitly rather than by reinterpreting the float array's memory:
    /// two App Service instances are not guaranteed to share an architecture, and a
    /// platform-endian blob would silently produce nonsense similarities on the one that
    /// disagreed rather than failing.
    /// </para>
    /// </summary>
    public static byte[] ToBytes(ReadOnlySpan<float> vector)
    {
        if (vector.IsEmpty)
        {
            return [];
        }

        var bytes = new byte[vector.Length * sizeof(float)];
        for (var i = 0; i < vector.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), vector[i]);
        }

        return bytes;
    }

    /// <summary>Unpacks a vector written by <see cref="ToBytes"/>. Absent or malformed reads as empty.</summary>
    public static float[] FromBytes(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0 || bytes.Length % sizeof(float) != 0)
        {
            return [];
        }

        var vector = new float[bytes.Length / sizeof(float)];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float)));
        }

        return vector;
    }

    /// <summary>
    /// Cosine similarity in [-1, 1], or 0 when either side is empty or has no magnitude.
    /// <para>
    /// Cosine rather than euclidean distance because embedding models encode meaning in
    /// <em>direction</em>: "extremely bizarre" and "slightly bizarre" sit at different distances
    /// from the origin but nearly the same direction, and it is the direction that says these are
    /// the same kind of strange.
    /// </para>
    /// </summary>
    public static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.IsEmpty || b.IsEmpty || a.Length != b.Length)
        {
            return 0;
        }

        double dot = 0, magnitudeA = 0, magnitudeB = 0;

        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            magnitudeA += a[i] * (double)a[i];
            magnitudeB += b[i] * (double)b[i];
        }

        if (magnitudeA <= 0 || magnitudeB <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
    }
}
