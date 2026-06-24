namespace RyzenTunerNext.Core.Models;

/// <summary>
/// PM Table 读回的实际生效值
/// </summary>
public class ActualValues
{
    public float FastLimit { get; init; }
    public float FastValue { get; init; }
    public float SlowLimit { get; init; }
    public float SlowValue { get; init; }
    public float StapmLimit { get; init; }
    public float StapmValue { get; init; }
    public float TctlTemp { get; init; }
    public float TctlTempValue { get; init; }
    public float SocketPower { get; init; }

    public static ActualValues Empty => new();
}
