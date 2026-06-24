namespace RyzenTunerNext.Core.Models;

/// <summary>
/// 参数下发结果
/// </summary>
public class ApplyResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ActualValues? Actual { get; init; }

    public static ApplyResult SuccessResult(ActualValues actual) => new()
    {
        Success = true,
        Actual = actual
    };

    public static ApplyResult Failed(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}
