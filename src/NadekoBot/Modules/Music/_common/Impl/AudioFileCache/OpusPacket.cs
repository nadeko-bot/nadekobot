namespace NadekoBot.Modules.Music;

public static class OpusPacket
{
    // Frame lengths at 48 kHz by the config number in the TOC byte (RFC 6716, section 3.1).
    private static ReadOnlySpan<short> FrameSamples
        => [
            480, 960, 1920, 2880, 480, 960, 1920, 2880, 480, 960, 1920, 2880,
            480, 960, 480, 960,
            120, 240, 480, 960, 120, 240, 480, 960, 120, 240, 480, 960, 120, 240, 480, 960
        ];

    // The samples per channel at 48 kHz in one packet, or 0 when the packet is not valid.
    public static int GetSamplesPerChannel(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty)
            return 0;

        var toc = packet[0];
        var frames = (toc & 0x03) switch
        {
            0 => 1,
            1 or 2 => 2,
            _ => packet.Length > 1 ? packet[1] & 0x3F : 0
        };

        return frames * FrameSamples[toc >> 3];
    }
}
