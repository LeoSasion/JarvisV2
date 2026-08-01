using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Jarvis.DesktopStyleSession;

internal static class DesktopListViewTransport
{
    private const uint ListViewFirst = 0x1000;
    private const uint ListViewGetTextColor = ListViewFirst + 35;
    private const uint ListViewSetTextColor = ListViewFirst + 36;
    private const uint SendMessageTimeoutBlock = 0x0001;
    private const uint SendMessageTimeoutAbortIfHung = 0x0002;
    private const uint SendMessageTimeoutErrorOnExit = 0x0020;
    private const uint RedrawInvalidate = 0x0001;
    private const uint RedrawErase = 0x0004;
    private const uint RedrawAllChildren = 0x0080;
    private const uint RedrawUpdateNow = 0x0100;
    private const uint MessageTimeoutMilliseconds = 250;
    private const uint MessageTimeoutFlags =
        SendMessageTimeoutBlock |
        SendMessageTimeoutAbortIfHung |
        SendMessageTimeoutErrorOnExit;
    private const uint RedrawFlags =
        RedrawInvalidate |
        RedrawErase |
        RedrawAllChildren |
        RedrawUpdateNow;

    public static uint GetTextColor(nint folderViewWindow)
    {
        nuint result = SendScalarMessage(
            folderViewWindow,
            ListViewGetTextColor,
            nint.Zero);
        return unchecked((uint)result);
    }

    public static void SetTextColor(
        nint folderViewWindow,
        uint colorRef)
    {
        nuint result = SendScalarMessage(
            folderViewWindow,
            ListViewSetTextColor,
            unchecked((nint)(long)colorRef));
        if (result == 0)
        {
            throw new InvalidOperationException(
                "The desktop ListView rejected LVM_SETTEXTCOLOR.");
        }
    }

    public static void RedrawExactFolderView(nint folderViewWindow)
    {
        if (!RedrawWindow(
                folderViewWindow,
                nint.Zero,
                nint.Zero,
                RedrawFlags))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "RedrawWindow failed for the exact desktop FolderView.");
        }
    }

    private static nuint SendScalarMessage(
        nint windowHandle,
        uint message,
        nint scalarParameter)
    {
        nint dispatchResult = SendMessageTimeout(
            windowHandle,
            message,
            nuint.Zero,
            scalarParameter,
            MessageTimeoutFlags,
            MessageTimeoutMilliseconds,
            out nuint messageResult);
        if (dispatchResult == nint.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(
                error,
                $"SendMessageTimeoutW failed for message 0x{message:X4}.");
        }

        return messageResult;
    }

    [DllImport(
        "user32.dll",
        EntryPoint = "SendMessageTimeoutW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint windowHandle,
        uint message,
        nuint wordParameter,
        nint longParameter,
        uint flags,
        uint timeoutMilliseconds,
        out nuint messageResult);

    [DllImport(
        "user32.dll",
        EntryPoint = "RedrawWindow",
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(
        nint windowHandle,
        nint updateRectangle,
        nint updateRegion,
        uint flags);
}
