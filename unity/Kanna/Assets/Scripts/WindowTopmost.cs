using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

public class WindowTopmost : MonoBehaviour
{
    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    static readonly int HWND_TOPMOST = -1;   // 置顶
    static readonly int HWND_NOTOPMOST = -2; // 取消置顶
    const uint SWP_SHOWWINDOW = 0x0040;

    private IntPtr myWindowHandle = IntPtr.Zero;

    void Start()
    {
        GetWindowHandle();
        SetTopmost(true); // 默认开启置顶
    }

    // 查找当前进程的主窗口句柄
    void GetWindowHandle()
    {
        int currentProcessId = Process.GetCurrentProcess().Id;

        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint windowProcessId);
            if (windowProcessId == currentProcessId)
            {
                myWindowHandle = hWnd;
                return false; // 找到后停止枚举
            }
            return true;
        }, IntPtr.Zero);
    }

    // 设置 / 取消置顶
    public void SetTopmost(bool enable)
    {
        if (myWindowHandle == IntPtr.Zero) return;

        int mode = enable ? HWND_TOPMOST : HWND_NOTOPMOST;
        SetWindowPos(myWindowHandle, mode, 0, 0, 0, 0, SWP_SHOWWINDOW);
    }
}