using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace GrammarPoliceMod;

public static class KeySimulator
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private static readonly object _lock = new();

    private static readonly Dictionary<string, byte> KeyMap = new()
    {
        ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44,
        ["E"] = 0x45, ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48,
        ["I"] = 0x49, ["J"] = 0x4A, ["K"] = 0x4B, ["L"] = 0x4C,
        ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F, ["P"] = 0x50,
        ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
        ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58,
        ["Y"] = 0x59, ["Z"] = 0x5A,
        ["Alpha0"] = 0x30, ["Alpha1"] = 0x31, ["Alpha2"] = 0x32,
        ["Alpha3"] = 0x33, ["Alpha4"] = 0x34, ["Alpha5"] = 0x35,
        ["Alpha6"] = 0x36, ["Alpha7"] = 0x37, ["Alpha8"] = 0x38,
        ["Alpha9"] = 0x39,
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
        ["LeftShift"] = 0xA0, ["RightShift"] = 0xA1,
        ["LeftControl"] = 0xA2, ["RightControl"] = 0xA3,
        ["LeftAlt"] = 0xA4, ["RightAlt"] = 0xA5,
        ["Space"] = 0x20, ["Return"] = 0x0D, ["Escape"] = 0x1B,
        ["Tab"] = 0x09, ["Backspace"] = 0x08,
        ["UpArrow"] = 0x26, ["DownArrow"] = 0x28,
        ["LeftArrow"] = 0x25, ["RightArrow"] = 0x27,
        ["Mouse0"] = 0x01, ["Mouse1"] = 0x02, ["Mouse2"] = 0x04,
    };

    public static void PressSequence(string sequenceConfig, int delayMs = 50)
    {
        if (string.IsNullOrWhiteSpace(sequenceConfig)) return;
        lock (_lock)
        {
            var keyNames = sequenceConfig.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var name in keyNames)
            {
                if (KeyMap.TryGetValue(name, out byte vk))
                {
                    keybd_event(vk, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(delayMs);
                    keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Thread.Sleep(delayMs / 2);
                }
            }
        }
    }
}
