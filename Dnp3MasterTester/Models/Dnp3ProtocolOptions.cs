namespace Dnp3MasterTester.Models;

public enum DataBits
{
    Five = 5,
    Six = 6,
    Seven = 7,
    Eight = 8
}

public enum StopBits
{
    One,
    OnePointFive,
    Two
}

public enum Parity
{
    None,
    Odd,
    Even,
    Mark,
    Space
}

public enum FlowControl
{
    None,
    XonXoff,
    RequestToSend,
    RequestToSendXonXoff
}

public enum CommandMode
{
    SelectBeforeOperate,
    DirectOperate
}

public enum OpType
{
    PulseOn,
    PulseOff,
    LatchOn,
    LatchOff
}
