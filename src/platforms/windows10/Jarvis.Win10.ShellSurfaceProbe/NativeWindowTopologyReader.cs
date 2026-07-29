using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Jarvis.Win10.ShellSurfaceProbe;

internal static class NativeWindowTopologyReader
{
    internal const int MaximumNodes = 1024;
    internal const int MaximumDepth = 8;

    public static IReadOnlyList<nint> EnumerateTopLevelWindows()
    {
        List<nint> windows = [];
        EnumWindows(
            (windowHandle, _) =>
            {
                windows.Add(windowHandle);
                return true;
            },
            nint.Zero);
        return windows;
    }

    public static nint GetDesktopShellWindow() => GetShellWindow();

    public static SurfaceTreeObservation ReadSurface(
        string surfaceKind,
        nint rootWindow)
    {
        List<WindowNodeObservation> nodes = [];
        Queue<PendingNode> pending = new();
        pending.Enqueue(
            new PendingNode(
                rootWindow,
                "root",
                null,
                0,
                0));
        bool truncated = false;

        while (pending.Count > 0)
        {
            PendingNode current = pending.Dequeue();
            if (nodes.Count >= MaximumNodes)
            {
                truncated = true;
                break;
            }

            nodes.Add(ReadNode(current));
            if (current.Depth >= MaximumDepth)
            {
                if (HasDirectChild(current.WindowHandle))
                {
                    truncated = true;
                }

                continue;
            }

            int remainingSlots =
                MaximumNodes - nodes.Count - pending.Count;
            if (remainingSlots <= 0)
            {
                if (HasDirectChild(current.WindowHandle))
                {
                    truncated = true;
                }

                continue;
            }

            IReadOnlyList<nint> children =
                EnumerateDirectChildren(
                    current.WindowHandle,
                    remainingSlots,
                    out bool childListTruncated);
            truncated |= childListTruncated;
            for (int index = 0; index < children.Count; index++)
            {
                pending.Enqueue(
                    new PendingNode(
                        children[index],
                        $"{current.NodeKey}/{index}",
                        current.NodeKey,
                        current.Depth + 1,
                        index));
            }
        }

        WindowNodeObservation root = nodes[0];
        SortedDictionary<string, int> histogram =
            new(StringComparer.Ordinal);
        foreach (WindowNodeObservation node in nodes)
        {
            histogram[node.ClassName] =
                histogram.TryGetValue(node.ClassName, out int count)
                    ? count + 1
                    : 1;
        }

        return new SurfaceTreeObservation(
            surfaceKind,
            root.ClassName,
            root.WindowHandle,
            root.ProcessId,
            root.ThreadId,
            nodes.Count,
            nodes.Max(node => node.Depth),
            truncated,
            ComputeTopologyHash(nodes),
            histogram,
            nodes);
    }

    public static string GetWindowClass(nint windowHandle)
    {
        StringBuilder buffer = new(256);
        int length =
            GetClassName(windowHandle, buffer, buffer.Capacity);
        return length <= 0
            ? "<unavailable>"
            : buffer.ToString(0, length);
    }

    public static uint GetWindowProcessId(
        nint windowHandle,
        out uint threadId)
    {
        threadId =
            GetWindowThreadProcessId(windowHandle, out uint processId);
        return processId;
    }

    private static WindowNodeObservation ReadNode(PendingNode pending)
    {
        uint processId =
            GetWindowProcessId(pending.WindowHandle, out uint threadId);
        _ = GetWindowRect(
            pending.WindowHandle,
            out NativeRectangle rectangle);

        return new WindowNodeObservation(
            pending.NodeKey,
            pending.ParentKey,
            pending.Depth,
            pending.SiblingOrdinal,
            ToHex(pending.WindowHandle),
            GetWindowClass(pending.WindowHandle),
            IsWindowVisible(pending.WindowHandle),
            processId,
            threadId,
            new WindowRectangle(
                rectangle.Left,
                rectangle.Top,
                rectangle.Right,
                rectangle.Bottom));
    }

    private static IReadOnlyList<nint> EnumerateDirectChildren(
        nint parentWindow,
        int maximumCount,
        out bool truncated)
    {
        List<nint> children = [];
        nint child = nint.Zero;
        while (children.Count < maximumCount)
        {
            child = FindWindowEx(
                parentWindow,
                child,
                null,
                null);
            if (child == nint.Zero)
            {
                truncated = false;
                return children;
            }

            children.Add(child);
        }

        truncated =
            FindWindowEx(
                parentWindow,
                child,
                null,
                null) != nint.Zero;
        return children;
    }

    private static bool HasDirectChild(nint parentWindow) =>
        FindWindowEx(
            parentWindow,
            nint.Zero,
            null,
            null) != nint.Zero;

    private static string ComputeTopologyHash(
        IEnumerable<WindowNodeObservation> nodes)
    {
        StringBuilder canonical = new();
        foreach (WindowNodeObservation node in nodes)
        {
            _ = canonical
                .Append(node.NodeKey).Append('|')
                .Append(node.ParentKey).Append('|')
                .Append(node.Depth).Append('|')
                .Append(node.SiblingOrdinal).Append('|')
                .Append(node.ClassName).Append('|')
                .Append(node.Visible ? '1' : '0').Append('|')
                .Append(node.Rectangle.Left).Append('|')
                .Append(node.Rectangle.Top).Append('|')
                .Append(node.Rectangle.Width).Append('|')
                .Append(node.Rectangle.Height)
                .AppendLine();
        }

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static string ToHex(nint windowHandle) =>
        $"0x{unchecked((ulong)windowHandle.ToInt64()):X}";

    private sealed record PendingNode(
        nint WindowHandle,
        string NodeKey,
        string? ParentKey,
        int Depth,
        int SiblingOrdinal);

    private delegate bool EnumWindowsCallback(
        nint windowHandle,
        nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsCallback callback,
        nint parameter);

    [DllImport(
        "user32.dll",
        EntryPoint = "FindWindowExW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern nint FindWindowEx(
        nint parentWindow,
        nint childAfter,
        string? className,
        string? windowName);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetClassNameW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern int GetClassName(
        nint windowHandle,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(
        nint windowHandle,
        out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        nint windowHandle,
        out NativeRectangle rectangle);
}
