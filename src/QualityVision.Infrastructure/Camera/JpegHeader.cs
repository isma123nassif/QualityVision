using System.Buffers.Binary;

namespace QualityVision.Infrastructure.Camera;

/// <summary>
/// Reads the image size from a JPEG's SOFn header without decoding any pixels. Used to reject decompression bombs
/// (a small file declaring a huge image) before <c>Cv2.ImDecode</c> allocates the full bitmap.
/// </summary>
public static class JpegHeader
{
    /// <summary>
    /// Walks the JPEG marker segments up to the first SOFn frame header (FF C0..C3, C5..C7, C9..CB, CD..CF) and returns
    /// its dimensions. Returns <c>false</c> if the data does not start with SOI, the structure is truncated or
    /// malformed, scan data starts before a frame header, or a side is 0 (height defined later by DNL is not supported).
    /// </summary>
    public static bool TryReadSize(ReadOnlySpan<byte> jpeg, out int width, out int height)
    {
        width = 0;
        height = 0;

        // SOI.
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return false;
        }

        int pos = 2;
        while (pos < jpeg.Length)
        {
            if (jpeg[pos] != 0xFF)
            {
                return false; // Every segment must start with a marker.
            }

            // Any number of 0xFF fill bytes may precede a marker code.
            while (pos < jpeg.Length && jpeg[pos] == 0xFF)
            {
                pos++;
            }

            if (pos >= jpeg.Length)
            {
                return false;
            }

            byte marker = jpeg[pos];
            int markerStart = pos - 1; // Position of the 0xFF directly before the marker code.
            pos++;

            // Standalone markers without a length field: TEM, RST0..7.
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            // SOI again, EOI, SOS (entropy-coded data follows) or the reserved 0x00: no frame header before it.
            if (marker is 0x00 or 0xD8 or 0xD9 or 0xDA)
            {
                return false;
            }

            if (pos + 2 > jpeg.Length)
            {
                return false;
            }

            int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(pos, 2)); // Includes its own 2 bytes.
            if (segmentLength < 2)
            {
                return false;
            }

            if (IsStartOfFrame(marker))
            {
                // FF Cn | length(2) | precision(1) | height(2) at +5 | width(2) at +7.
                if (segmentLength < 7 || markerStart + 9 > jpeg.Length)
                {
                    return false;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(markerStart + 5, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(markerStart + 7, 2));
                if (width == 0 || height == 0)
                {
                    width = 0;
                    height = 0;
                    return false;
                }

                return true;
            }

            pos += segmentLength;
        }

        return false;
    }

    // SOF0..3, SOF5..7, SOF9..11, SOF13..15. C4 (DHT), C8 (JPG) and CC (DAC) share the range but are not frame headers.
    private static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
}
