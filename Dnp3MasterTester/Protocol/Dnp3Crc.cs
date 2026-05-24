namespace Dnp3MasterTester.Protocol;

internal static class Dnp3Crc
{
    private const ushort Polynomial = 0xA6BC;

    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x0001) != 0
                    ? (ushort)((crc >> 1) ^ Polynomial)
                    : (ushort)(crc >> 1);
            }
        }

        return (ushort)~crc;
    }

    public static bool Matches(ReadOnlySpan<byte> data, byte low, byte high)
    {
        var crc = Compute(data);
        return (byte)crc == low && (byte)(crc >> 8) == high;
    }
}
