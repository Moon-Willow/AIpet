using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;


public class TransparancyManager : MonoBehaviour
{
#if !UNITY_EDITOR
    [DllImport("user32.dll")]
    static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    const int GWL_EXSTYLE = -20;
    const int WS_EX_LAYERED = 0x80000;
    // Remove WS_EX_TRANSPARENT to allow window interaction
    const int LWA_COLORKEY = 0x1;
    const int LWA_ALPHA = 0x2;
#endif

    void Start()
    {
#if !UNITY_EDITOR
        IntPtr hwnd = GetActiveWindow();
        // Make window layered but not input-transparent
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED);

        // Adjust color key and alpha for selective transparency
        SetLayeredWindowAttributes(hwnd, 0, 255, LWA_COLORKEY); // You might not need LWA_COLORKEY
#endif
    }
}