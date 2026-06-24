using System.Runtime.InteropServices;

namespace RyzenTunerNext.Core.Services;

/// <summary>
/// libryzenadj.dll 原生 P/Invoke 声明
/// </summary>
internal static class RyzenAdjNative
{
    private const string DllName = "libryzenadj.dll";

    // ===== 生命周期 =====

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr init_ryzenadj();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void cleanup_ryzenadj(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int get_cpu_family(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int get_bios_if_ver(IntPtr ry);

    // ===== 参数设置 (单位: mW / °C) =====

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int set_fast_limit(IntPtr ry, uint value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int set_slow_limit(IntPtr ry, uint value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int set_stapm_limit(IntPtr ry, uint value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int set_tctl_temp(IntPtr ry, uint value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int set_power_saving(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int set_max_performance(IntPtr ry);

    // ===== PM Table 操作 =====

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int init_table(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int refresh_table(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint get_table_ver(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint get_table_size(IntPtr ry);

    // ===== PM Table 读取 (返回 float, 单位: mW / °C / MHz) =====

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_stapm_limit(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_stapm_value(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_fast_limit(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_fast_value(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_slow_limit(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_slow_value(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_tctl_temp(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_tctl_temp_value(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_socket_power(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_soc_power(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_core_clk(IntPtr ry, uint core);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_core_temp(IntPtr ry, uint core);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_core_power(IntPtr ry, uint core);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_core_volt(IntPtr ry, uint core);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_gfx_clk(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_gfx_temp(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_mem_clk(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_fclk(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_soc_volt(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_cclk_setpoint(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_cclk_busy_value(IntPtr ry);
}
