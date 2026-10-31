using System.IO;
using System.Runtime.InteropServices;

namespace VideoSynthesizer;

/// <summary>Documented NDI C ABI subset, loaded only when the user enables output.</summary>
public sealed class NdiRuntime
{
    [StructLayout(LayoutKind.Sequential)]
    public struct VideoFrame
    {
        public int Width, Height, FourCC, FrameRateN, FrameRateD;
        public float AspectRatio;
        public int FrameFormat;
        public long Timecode;
        public IntPtr Data;
        public int Stride;
        public IntPtr Metadata;
        public long Timestamp;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct SendCreate
    {
        public IntPtr Name, Groups;
        [MarshalAs(UnmanagedType.I1)] public bool ClockVideo;
        [MarshalAs(UnmanagedType.I1)] public bool ClockAudio;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Tally
    {
        [MarshalAs(UnmanagedType.I1)] public bool Program;
        [MarshalAs(UnmanagedType.I1)] public bool Preview;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] private delegate bool Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr CreateSender(ref SendCreate settings);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DestroySender(IntPtr sender);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void SendVideo(IntPtr sender, ref VideoFrame frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int Connections(IntPtr sender, uint timeout);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] public delegate bool GetTally(IntPtr sender, out Tally tally, uint timeout);

    private readonly IntPtr library;
    public readonly CreateSender Create;
    public readonly DestroySender Destroy;
    public readonly SendVideo Send;
    public readonly Connections GetConnections;
    public readonly GetTally ReadTally;
    private static readonly object Gate = new();
    private static NdiRuntime? instance;

    private NdiRuntime(string path)
    {
        library = NativeLibrary.Load(path);
        try
        {
            Create = Export<CreateSender>("NDIlib_send_create");
            Destroy = Export<DestroySender>("NDIlib_send_destroy");
            Send = Export<SendVideo>("NDIlib_send_send_video_v2");
            GetConnections = Export<Connections>("NDIlib_send_get_no_connections");
            ReadTally = Export<GetTally>("NDIlib_send_get_tally");
            if (!Export<Initialize>("NDIlib_initialize")()) throw new InvalidOperationException("NDI runtime initialization failed.");
        }
        catch { NativeLibrary.Free(library); throw; }
        // Keep the initialized runtime for process lifetime. One window stopping
        // must not destroy runtime state used by another window or sender.
    }

    public T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    public static NdiRuntime Load(string? explicitPath = null)
    {
        lock (Gate)
        {
            if (instance != null) return instance;
            string filename = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "Processing.NDI.Lib.arm64.dll" : "Processing.NDI.Lib.x64.dll";
            string? configured = explicitPath ?? Environment.GetEnvironmentVariable("VIDEO_SYNTH_NDI_RUNTIME");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (!Path.IsPathFullyQualified(configured)) throw new ArgumentException("Choose an absolute NDI runtime path.");
                return instance = new NdiRuntime(configured);
            }
            var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, filename) };
            foreach (string variable in new[] { "NDI_RUNTIME_DIR_V6", "NDI_RUNTIME_DIR_V5" })
            {
                string? directory = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrEmpty(directory) && Path.IsPathFullyQualified(directory)) candidates.Add(Path.Combine(directory, filename));
            }
            string? found = candidates.Find(File.Exists);
            if (found == null) throw new FileNotFoundException("NDI runtime not found. Install the official NDI Runtime or choose its DLL with ‘NDI runtime…’.");
            return instance = new NdiRuntime(found);
        }
    }
}

public sealed class NdiSender : IDisposable
{
    private readonly NdiRuntime runtime;
    private IntPtr sender;
    public IntPtr Handle => sender;
    public NdiSender(NdiRuntime runtime, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Contains('\0')) throw new ArgumentException("Invalid NDI source name.");
        this.runtime = runtime;
        IntPtr text = Marshal.StringToCoTaskMemUTF8(name);
        try { var create = new NdiRuntime.SendCreate { Name = text }; sender = runtime.Create(ref create); }
        finally { Marshal.FreeCoTaskMem(text); }
        if (sender == IntPtr.Zero) throw new InvalidOperationException("NDI sender could not be created.");
    }
    public void Send(byte[] bgra, int width, int height, int fps)
    {
        if (sender == IntPtr.Zero) throw new ObjectDisposedException(nameof(NdiSender));
        if (width < 2 || width > 1920 || width % 2 != 0 || height < 1 || height > 1080 || bgra.Length != width * height * 4 || fps < 1 || fps > 60)
            throw new ArgumentException("Invalid NDI video frame.");
        var pin = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            var frame = new NdiRuntime.VideoFrame { Width = width, Height = height, FourCC = 0x41524742,
                FrameRateN = fps, FrameRateD = 1, AspectRatio = (float)width / height, FrameFormat = 1,
                Timecode = long.MaxValue, Data = pin.AddrOfPinnedObject(), Stride = width * 4 };
            runtime.Send(sender, ref frame); // Synchronous: the SDK is finished with the buffer on return.
        }
        finally { pin.Free(); }
    }
    public (int Receivers, bool Program, bool Preview) Telemetry()
    {
        runtime.ReadTally(sender, out var tally, 0);
        return (runtime.GetConnections(sender, 0), tally.Program, tally.Preview);
    }
    public void Dispose() { if (sender != IntPtr.Zero) { runtime.Destroy(sender); sender = IntPtr.Zero; } }
}
