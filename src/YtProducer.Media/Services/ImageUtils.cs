using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace YtProducer.Media.Services;

public static class ImageUtils
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static async Task<RgbaImage> LoadImageAsync(
        string ffmpegPath,
        string ffprobePath,
        FfmpegRunner runner,
        string imagePath,
        CancellationToken cancellationToken)
    {
        var probeArgs = new[]
        {
            "-v", "error",
            "-select_streams", "v:0",
            "-show_entries", "stream=width,height",
            "-of", "csv=p=0:s=x",
            imagePath
        };

        var probe = await runner.RunAsync(ffprobePath, probeArgs, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (probe.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffprobe image probe failed: {probe.StdErr}");
        }

        var sizeToken = probe.StdOut.Trim();
        var parts = sizeToken.Split('x', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) ||
            width <= 0 ||
            height <= 0)
        {
            throw new InvalidOperationException($"Unable to parse image dimensions from ffprobe output: '{sizeToken}'");
        }

        var decodeArgs = new[]
        {
            "-v", "error",
            "-i", imagePath,
            "-vframes", "1",
            "-f", "rawvideo",
            "-pix_fmt", "rgba",
            "pipe:1"
        };

        var decoded = await runner.RunBinaryAsync(ffmpegPath, decodeArgs, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (decoded.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg image decode failed: {decoded.StdErr}");
        }

        var expected = checked(width * height * 4);
        if (decoded.StdOutBytes.Length < expected)
        {
            throw new InvalidOperationException(
                $"Decoded image byte length {decoded.StdOutBytes.Length} is smaller than expected {expected}.");
        }

        var pixels = new byte[expected];
        Buffer.BlockCopy(decoded.StdOutBytes, 0, pixels, 0, expected);

        return new RgbaImage(width, height, pixels);
    }

    public static void SaveRgbaAsPng(string outputPath, int width, int height, byte[] rgba)
    {
        var expectedSize = checked(width * height * 4);
        if (rgba.Length != expectedSize)
        {
            throw new ArgumentException("RGBA buffer has unexpected size.", nameof(rgba));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory());

        using var fileStream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        fileStream.Write(signature);

        var ihdr = new byte[13];
        WriteInt32BigEndian(ihdr, 0, width);
        WriteInt32BigEndian(ihdr, 4, height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type RGBA
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // interlace

        WriteChunk(fileStream, "IHDR", ihdr);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var stride = width * 4;
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0); // filter type 0 (None)
                zlib.Write(rgba, y * stride, stride);
            }
        }

        WriteChunk(fileStream, "IDAT", compressed.ToArray());
        WriteChunk(fileStream, "IEND", Array.Empty<byte>());
    }

    public static double CalculateCoverScale(int imageWidth, int imageHeight, int targetWidth, int targetHeight)
    {
        var scaleX = targetWidth / (double)imageWidth;
        var scaleY = targetHeight / (double)imageHeight;
        return Math.Max(scaleX, scaleY);
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> lenBytes = stackalloc byte[4];
        WriteInt32BigEndian(lenBytes, data.Length);
        output.Write(lenBytes);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = ComputeCrc(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        WriteInt32BigEndian(crcBytes, unchecked((int)crc));
        output.Write(crcBytes);
    }

    private static void WriteInt32BigEndian(byte[] buffer, int index, int value)
    {
        buffer[index] = (byte)((value >> 24) & 0xFF);
        buffer[index + 1] = (byte)((value >> 16) & 0xFF);
        buffer[index + 2] = (byte)((value >> 8) & 0xFF);
        buffer[index + 3] = (byte)(value & 0xFF);
    }

    private static void WriteInt32BigEndian(Span<byte> buffer, int value)
    {
        buffer[0] = (byte)((value >> 24) & 0xFF);
        buffer[1] = (byte)((value >> 16) & 0xFF);
        buffer[2] = (byte)((value >> 8) & 0xFF);
        buffer[3] = (byte)(value & 0xFF);
    }

    private static uint ComputeCrc(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;

        for (var i = 0; i < type.Length; i++)
        {
            crc = CrcTable[(crc ^ type[i]) & 0xFF] ^ (crc >> 8);
        }

        for (var i = 0; i < data.Length; i++)
        {
            crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var j = 0; j < 8; j++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[i] = c;
        }

        return table;
    }
}

public sealed record RgbaImage(int Width, int Height, byte[] Pixels);
