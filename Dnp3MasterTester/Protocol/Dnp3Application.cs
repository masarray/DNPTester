using System.Buffers.Binary;
using System.Text;
using Dnp3MasterTester.Models;

namespace Dnp3MasterTester.Protocol;

internal enum Dnp3ReadReason
{
    StartupIntegrity,
    ManualIntegrity,
    EventPoll,
    StaticRefresh,
    LinkStatus,
    Command
}

internal sealed record Dnp3ApplicationResponse(
    byte FunctionCode,
    byte Sequence,
    ushort InternalIndications,
    IReadOnlyList<Dnp3Measurement> Measurements,
    IReadOnlyList<Dnp3CommandStatus> CommandStatuses);

internal sealed record Dnp3Measurement(
    string PointType,
    ushort Index,
    string Value,
    string Flags,
    SourceTimestampInfo Timestamp,
    string Variation,
    string Qualifier,
    string Status = "-");

internal sealed record Dnp3CommandStatus(ushort Index, string Operation, string Status, string Variation);

internal static class Dnp3Application
{
    public const byte FunctionConfirm = 0x00;
    public const byte FunctionRead = 0x01;
    public const byte FunctionSelect = 0x03;
    public const byte FunctionOperate = 0x04;
    public const byte FunctionDirectOperate = 0x05;
    public const byte FunctionResponse = 0x81;
    public const byte FunctionUnsolicitedResponse = 0x82;

    public static byte[] BuildReadRequest(byte sequence, bool class0, bool class1, bool class2, bool class3)
    {
        var body = new List<byte> { RequestControl(sequence), FunctionRead };
        if (class0)
        {
            AddAllObjects(body, 60, 1);
        }

        if (class1)
        {
            AddAllObjects(body, 60, 2);
        }

        if (class2)
        {
            AddAllObjects(body, 60, 3);
        }

        if (class3)
        {
            AddAllObjects(body, 60, 4);
        }

        return body.ToArray();
    }

    public static byte[] BuildConfirm(byte sequence) => [RequestControl(sequence), FunctionConfirm];

    public static byte[] BuildBinaryCommand(byte sequence, byte functionCode, ushort index, OpType operation)
    {
        var body = new List<byte>
        {
            RequestControl(sequence),
            functionCode,
            12,
            1,
            0x28,
            1,
            0,
            (byte)index,
            (byte)(index >> 8),
            ToControlCode(operation),
            1
        };

        body.AddRange(BitConverter.GetBytes((uint)(operation is OpType.PulseOn or OpType.PulseOff ? 1000 : 0)));
        body.AddRange(BitConverter.GetBytes((uint)0));
        body.Add(0);
        return body.ToArray();
    }

    public static Dnp3ApplicationResponse Decode(ReadOnlySpan<byte> transportPayload)
    {
        if (transportPayload.Length < 4)
        {
            return new Dnp3ApplicationResponse(0, 0, 0, [], []);
        }

        var appControl = transportPayload[0];
        var function = transportPayload[1];
        var sequence = (byte)(appControl & 0x0F);
        var iin = BinaryPrimitives.ReadUInt16LittleEndian(transportPayload[2..4]);
        var measurements = new List<Dnp3Measurement>();
        var commandStatuses = new List<Dnp3CommandStatus>();
        var body = transportPayload[4..];
        var offset = 0;

        while (offset + 3 <= body.Length)
        {
            var group = body[offset++];
            var variation = body[offset++];
            var qualifier = body[offset++];
            var qualifierText = $"0x{qualifier:X2}";
            var variationText = $"g{group}v{variation}";

            if (qualifier is 0x17 or 0x28)
            {
                if (!TryDecodePrefixedObjects(body, group, variation, qualifier, qualifierText, variationText, ref offset, measurements, commandStatuses))
                {
                    break;
                }

                continue;
            }

            if (!TryReadIndexes(body, qualifier, ref offset, out var indexes))
            {
                break;
            }

            foreach (var index in indexes)
            {
                if (!TryDecodeObject(body, group, variation, index, qualifierText, variationText, ref offset, measurements, commandStatuses))
                {
                    return new Dnp3ApplicationResponse(function, sequence, iin, measurements, commandStatuses);
                }
            }
        }

        return new Dnp3ApplicationResponse(function, sequence, iin, measurements, commandStatuses);
    }

    private static byte RequestControl(byte sequence) => (byte)(0xC0 | (sequence & 0x0F));

    private static void AddAllObjects(List<byte> body, byte group, byte variation)
    {
        body.Add(group);
        body.Add(variation);
        body.Add(0x06);
    }

    private static byte ToControlCode(OpType operation) => operation switch
    {
        OpType.PulseOn => 0x01,
        OpType.PulseOff => 0x02,
        OpType.LatchOn => 0x03,
        OpType.LatchOff => 0x04,
        _ => 0x03
    };

    private static bool TryReadIndexes(ReadOnlySpan<byte> body, byte qualifier, ref int offset, out IReadOnlyList<ushort> indexes)
    {
        indexes = Array.Empty<ushort>();
        switch (qualifier)
        {
            case 0x00:
                if (offset + 2 > body.Length) return false;
                indexes = Enumerable.Range(body[offset], body[offset + 1] - body[offset] + 1).Select(x => (ushort)x).ToArray();
                offset += 2;
                return true;
            case 0x01:
                if (offset + 4 > body.Length) return false;
                var start16 = BinaryPrimitives.ReadUInt16LittleEndian(body[offset..]);
                var stop16 = BinaryPrimitives.ReadUInt16LittleEndian(body[(offset + 2)..]);
                indexes = Enumerable.Range(start16, stop16 - start16 + 1).Select(x => (ushort)x).ToArray();
                offset += 4;
                return true;
            case 0x07:
                if (offset + 1 > body.Length) return false;
                indexes = Enumerable.Range(0, body[offset++]).Select(x => (ushort)x).ToArray();
                return true;
            case 0x08:
                if (offset + 2 > body.Length) return false;
                var count16 = BinaryPrimitives.ReadUInt16LittleEndian(body[offset..]);
                indexes = Enumerable.Range(0, count16).Select(x => (ushort)x).ToArray();
                offset += 2;
                return true;
            case 0x17:
            case 0x28:
                return false;
            case 0x06:
                indexes = Array.Empty<ushort>();
                return true;
            default:
                return false;
        }
    }

    private static bool TryDecodePrefixedObjects(
        ReadOnlySpan<byte> body,
        byte group,
        byte variation,
        byte qualifier,
        string qualifierText,
        string variationText,
        ref int offset,
        List<Dnp3Measurement> measurements,
        List<Dnp3CommandStatus> commandStatuses)
    {
        int count;
        if (qualifier == 0x17)
        {
            if (offset + 1 > body.Length) return false;
            count = body[offset++];
        }
        else
        {
            if (offset + 2 > body.Length) return false;
            count = BinaryPrimitives.ReadUInt16LittleEndian(body[offset..]);
            offset += 2;
        }

        for (var i = 0; i < count; i++)
        {
            ushort index;
            if (qualifier == 0x17)
            {
                if (offset + 1 > body.Length) return false;
                index = body[offset++];
            }
            else
            {
                if (offset + 2 > body.Length) return false;
                index = BinaryPrimitives.ReadUInt16LittleEndian(body[offset..]);
                offset += 2;
            }

            if (!TryDecodeObject(body, group, variation, index, qualifierText, variationText, ref offset, measurements, commandStatuses))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryDecodeObject(
        ReadOnlySpan<byte> body,
        byte group,
        byte variation,
        ushort index,
        string qualifier,
        string variationText,
        ref int offset,
        List<Dnp3Measurement> measurements,
        List<Dnp3CommandStatus> commandStatuses)
    {
        return group switch
        {
            1 => DecodeBinary(body, variation, index, qualifier, variationText, ref offset, measurements, "Binary Input", hasTime: false),
            2 => DecodeBinary(body, variation, index, qualifier, variationText, ref offset, measurements, "Binary Input", hasTime: variation is 2 or 3),
            10 => DecodeBinary(body, variation, index, qualifier, variationText, ref offset, measurements, "Binary Output Status", hasTime: false),
            12 => DecodeCommandStatus(body, index, variationText, ref offset, measurements, commandStatuses),
            20 => DecodeUnsigned(body, variation, index, qualifier, variationText, ref offset, measurements, "Counter", hasTime: false),
            22 => DecodeUnsigned(body, variation, index, qualifier, variationText, ref offset, measurements, "Counter", hasTime: variation is 2 or 6),
            30 => DecodeAnalog(body, variation, index, qualifier, variationText, ref offset, measurements, "Analog Input", hasTime: false),
            32 => DecodeAnalog(body, variation, index, qualifier, variationText, ref offset, measurements, "Analog Input", hasTime: variation is 3 or 4 or 7 or 8),
            40 => DecodeAnalog(body, variation, index, qualifier, variationText, ref offset, measurements, "Analog Output Status", hasTime: false),
            42 => DecodeAnalog(body, variation, index, qualifier, variationText, ref offset, measurements, "Analog Output Status", hasTime: variation is 3 or 4 or 7 or 8),
            _ => SkipUnsupported(body, group, variation, ref offset)
        };
    }

    private static bool DecodeBinary(ReadOnlySpan<byte> body, byte variation, ushort index, string qualifier, string variationText, ref int offset, List<Dnp3Measurement> measurements, string pointType, bool hasTime)
    {
        if (offset + 1 > body.Length) return false;
        var flags = body[offset++];
        var timestamp = hasTime ? ReadTimestamp(body, ref offset) : SourceTimestampInfo.NotSupplied("-");
        measurements.Add(new Dnp3Measurement(pointType, index, ((flags & 0x80) != 0).ToString(), flags.ToString(), timestamp, variationText, qualifier));
        return true;
    }

    private static bool DecodeUnsigned(ReadOnlySpan<byte> body, byte variation, ushort index, string qualifier, string variationText, ref int offset, List<Dnp3Measurement> measurements, string pointType, bool hasTime)
    {
        if (offset + 1 > body.Length) return false;
        var flags = body[offset++];
        uint value;
        if (variation is 2 or 6)
        {
            if (offset + 2 > body.Length) return false;
            value = BinaryPrimitives.ReadUInt16LittleEndian(body[offset..]);
            offset += 2;
        }
        else
        {
            if (offset + 4 > body.Length) return false;
            value = BinaryPrimitives.ReadUInt32LittleEndian(body[offset..]);
            offset += 4;
        }

        var timestamp = hasTime ? ReadTimestamp(body, ref offset) : SourceTimestampInfo.NotSupplied("-");
        measurements.Add(new Dnp3Measurement(pointType, index, value.ToString(), flags.ToString(), timestamp, variationText, qualifier));
        return true;
    }

    private static bool DecodeAnalog(ReadOnlySpan<byte> body, byte variation, ushort index, string qualifier, string variationText, ref int offset, List<Dnp3Measurement> measurements, string pointType, bool hasTime)
    {
        if (offset + 1 > body.Length) return false;
        var flags = body[offset++];
        string value;

        if (variation is 2 or 4)
        {
            if (offset + 2 > body.Length) return false;
            value = BinaryPrimitives.ReadInt16LittleEndian(body[offset..]).ToString();
            offset += 2;
        }
        else if (variation is 5 or 7)
        {
            if (offset + 4 > body.Length) return false;
            value = BitConverter.ToSingle(body.Slice(offset, 4)).ToString("G");
            offset += 4;
        }
        else if (variation is 6 or 8)
        {
            if (offset + 8 > body.Length) return false;
            value = BitConverter.ToDouble(body.Slice(offset, 8)).ToString("G");
            offset += 8;
        }
        else
        {
            if (offset + 4 > body.Length) return false;
            value = BinaryPrimitives.ReadInt32LittleEndian(body[offset..]).ToString();
            offset += 4;
        }

        var timestamp = hasTime ? ReadTimestamp(body, ref offset) : SourceTimestampInfo.NotSupplied("-");
        measurements.Add(new Dnp3Measurement(pointType, index, value, flags.ToString(), timestamp, variationText, qualifier));
        return true;
    }

    private static bool DecodeCommandStatus(ReadOnlySpan<byte> body, ushort index, string variationText, ref int offset, List<Dnp3Measurement> measurements, List<Dnp3CommandStatus> commandStatuses)
    {
        if (offset + 11 > body.Length) return false;
        var control = body[offset++];
        offset += 1 + 4 + 4;
        var status = body[offset++];
        var operation = ControlCodeText(control);
        var statusText = CommandStatusText(status);
        commandStatuses.Add(new Dnp3CommandStatus(index, operation, statusText, variationText));
        measurements.Add(new Dnp3Measurement("Binary Command Event", index, ExpectedValue(operation), statusText, SourceTimestampInfo.NotSupplied("-"), variationText, "0x28", statusText));
        return true;
    }

    private static SourceTimestampInfo ReadTimestamp(ReadOnlySpan<byte> body, ref int offset)
    {
        if (offset + 6 > body.Length)
        {
            return SourceTimestampInfo.Unknown("Malformed");
        }

        ulong value = 0;
        for (var i = 0; i < 6; i++)
        {
            value |= (ulong)body[offset + i] << (8 * i);
        }

        offset += 6;
        try
        {
            return SourceTimestampInfo.Valid(DateTimeOffset.FromUnixTimeMilliseconds((long)value).LocalDateTime, "SynchronizedTime");
        }
        catch (ArgumentOutOfRangeException)
        {
            return SourceTimestampInfo.Invalid("InvalidTime");
        }
    }

    private static bool SkipUnsupported(ReadOnlySpan<byte> body, byte group, byte variation, ref int offset)
    {
        var itemSize = (group, variation) switch
        {
            (50, 1) => 6,
            (51, 1) => 6,
            (80, 1) => 1,
            (110, _) => 0,
            _ => -1
        };

        if (itemSize < 0 || offset + itemSize > body.Length)
        {
            return false;
        }

        offset += itemSize;
        return true;
    }

    private static string ControlCodeText(byte control) => (control & 0x0F) switch
    {
        1 => nameof(OpType.PulseOn),
        2 => nameof(OpType.PulseOff),
        3 => nameof(OpType.LatchOn),
        4 => nameof(OpType.LatchOff),
        _ => $"ControlCode({control})"
    };

    private static string ExpectedValue(string operation) => operation is nameof(OpType.LatchOff) or nameof(OpType.PulseOff) ? bool.FalseString : bool.TrueString;

    private static string CommandStatusText(byte status) => status switch
    {
        0 => "Success",
        1 => "Timeout",
        2 => "NoSelect",
        3 => "FormatError",
        4 => "NotSupported",
        5 => "AlreadyActive",
        6 => "HardwareError",
        7 => "Local",
        8 => "TooManyOps",
        9 => "NotAuthorized",
        10 => "AutomationInhibit",
        11 => "ProcessingLimited",
        12 => "OutOfRange",
        126 => "DownstreamLocal",
        127 => "AlreadyComplete",
        _ => $"Status({status})"
    };
}
