using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public class SendInputMono : MonoBehaviour
{
    const uint INPUT_KEYBOARD = 1;

    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_UNICODE = 0x0004;

    const ushort VK_RETURN = 0x0D;
    const ushort VK_TAB = 0x09;
    const ushort VK_BACK = 0x08;

    [Header("Local Test")]
    [SerializeField] bool sendOnStart = true;
    [SerializeField] float startDelaySeconds = 2f;
    [SerializeField] KeyCode triggerKey = KeyCode.F8;
    [SerializeField] string testText = "Hello World";

    void Start()
    {
        if (sendOnStart)
        {
            // Leave a short delay so you can click the target input field first.
            Invoke(nameof(TestInput), startDelaySeconds);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(triggerKey))
        {
            TestInput();
        }
    }

    void TestInput()
    {
        bool ok = SendText(testText);
        Debug.Log($"[SendInputMono] TestInput finished, success={ok}, text={testText}");
    }

    public static bool SendText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            Debug.LogWarning("[SendInputMono] SendText skipped because text is empty.");
            return false;
        }

        var inputs = new List<INPUT>(text.Length * 2);

        foreach (char c in text)
        {
            AddCharInputs(inputs, c);
        }

        uint sentCount = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sentCount != inputs.Count)
        {
            int error = Marshal.GetLastWin32Error();
            Debug.LogError($"[SendInputMono] SendInput failed. sent={sentCount}/{inputs.Count}, error={error}");
            return false;
        }

        return true;
    }
    static void AddCharInputs(List<INPUT> inputs, char c)
    {
        switch (c)
        {
            case '\r':
                return;
            case '\n':
                AddVirtualKeyInputs(inputs, VK_RETURN);
                return;
            case '\t':
                AddVirtualKeyInputs(inputs, VK_TAB);
                return;
            case '\b':
                AddVirtualKeyInputs(inputs, VK_BACK);
                return;
            default:
                inputs.Add(CreateUnicodeInput(c, false));
                inputs.Add(CreateUnicodeInput(c, true));
                return;
        }
    }

    static void AddVirtualKeyInputs(List<INPUT> inputs, ushort virtualKey)
    {
        inputs.Add(CreateVirtualKeyInput(virtualKey, false));
        inputs.Add(CreateVirtualKeyInput(virtualKey, true));
    }

    static INPUT CreateUnicodeInput(char c, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    static INPUT CreateVirtualKeyInput(ushort virtualKey, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
