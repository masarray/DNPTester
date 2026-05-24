using System.Buffers;

namespace Dnp3MasterTester.Protocol;

internal static class Dnp3LinkLayer
{
    public const byte Start1 = 0x05;
    public const byte Start2 = 0x64;
    public const byte FuncResetLinkStates = 0x00;
    public const byte FuncConfirmedUserData = 0x03;
    public const byte FuncUnconfirmedUserData = 0x04;
    public const byte FuncRequestLinkStatus = 0x09;

    public static byte[] EncodePrimary(
        ushort source,
        ushort destination,
        byte function,
        ReadOnlySpan<byte> payload,
        bool useFrameCountBit)
    {
        var control = (byte)(0xC0 | function);
        if (function is FuncConfirmedUserData or FuncUnconfirmedUserData)
        {
            control |= 0x10;
            if (useFrameCountBit)
            {
                control |= 0x20;
            }
        }

        var length = checked((byte)(5 + payload.Length));
        var frame = new ArrayBufferWriter<byte>();
        Span<byte> header = stackalloc byte[8];
        header[0] = Start1;
        header[1] = Start2;
        header[2] = length;
        header[3] = control;
        WriteUInt16(header[4..], destination);
        WriteUInt16(header[6..], source);
        frame.Write(header);
        WriteCrc(frame, header);

        var remaining = payload;
        while (!remaining.IsEmpty)
        {
            var take = Math.Min(16, remaining.Length);
            var block = remaining[..take];
            frame.Write(block);
            WriteCrc(frame, block);
            remaining = remaining[take..];
        }

        return frame.WrittenSpan.ToArray();
    }

    public static bool TryDecode(ReadOnlySpan<byte> raw, out Dnp3LinkFrame frame)
    {
        frame = new Dnp3LinkFrame(0, 0, 0, []);
        if (raw.Length < 10 || raw[0] != Start1 || raw[1] != Start2)
        {
            return false;
        }

        var length = raw[2];
        if (length < 5)
        {
            return false;
        }

        if (!Dnp3Crc.Matches(raw[..8], raw[8], raw[9]))
        {
            return false;
        }

        var payloadLength = length - 5;
        var expectedLength = 10 + payloadLength + (int)Math.Ceiling(payloadLength / 16d) * 2;
        if (raw.Length < expectedLength)
        {
            return false;
        }

        var payload = new byte[payloadLength];
        var rawOffset = 10;
        var payloadOffset = 0;
        while (payloadOffset < payloadLength)
        {
            var take = Math.Min(16, payloadLength - payloadOffset);
            if (!Dnp3Crc.Matches(raw.Slice(rawOffset, take), raw[rawOffset + take], raw[rawOffset + take + 1]))
            {
                return false;
            }

            raw.Slice(rawOffset, take).CopyTo(payload.AsSpan(payloadOffset));
            rawOffset += take + 2;
            payloadOffset += take;
        }

        frame = new Dnp3LinkFrame(
            raw[3],
            ReadUInt16(raw[4..]),
            ReadUInt16(raw[6..]),
            payload);
        return true;
    }

    public static int GetEncodedLength(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 3 || buffer[0] != Start1 || buffer[1] != Start2)
        {
            return -1;
        }

        var payloadLength = buffer[2] - 5;
        return payloadLength < 0 ? -1 : 10 + payloadLength + (int)Math.Ceiling(payloadLength / 16d) * 2;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes) => (ushort)(bytes[0] | (bytes[1] << 8));

    private static void WriteUInt16(Span<byte> bytes, ushort value)
    {
        bytes[0] = (byte)value;
        bytes[1] = (byte)(value >> 8);
    }

    private static void WriteCrc(IBufferWriter<byte> writer, ReadOnlySpan<byte> block)
    {
        var crc = Dnp3Crc.Compute(block);
        Span<byte> bytes = stackalloc byte[] { (byte)crc, (byte)(crc >> 8) };
        writer.Write(bytes);
    }
}
