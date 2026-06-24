namespace RyzenTunerNext.Core.Models;

/// <summary>
/// RyzenAdj 错误码
/// </summary>
public enum RyzenAdjError
{
    Success = 0,
    FamUnsupported = -1,
    SmuTimeout = -2,
    SmuUnsupported = -3,
    SmuRejected = -4,
    MemoryAccess = -5,
}
