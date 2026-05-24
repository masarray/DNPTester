namespace Dnp3MasterTester.Protocol;

internal sealed record Dnp3LinkFrame(byte Control, ushort Destination, ushort Source, byte[] Payload)
{
    public bool IsPrimary => (Control & 0x40) != 0;

    public byte Function => (byte)(Control & 0x0F);
}
