using System.Diagnostics;
using System.Runtime.InteropServices;
using VideoSynthesizer;

static class NdiChecks
{
    [StructLayout(LayoutKind.Sequential)] private struct Source { public IntPtr Name, Address; }
    [StructLayout(LayoutKind.Sequential)] private struct ReceiveCreate
    {
        public Source Source;
        public int ColorFormat, Bandwidth;
        [MarshalAs(UnmanagedType.I1)] public bool AllowFields;
        public IntPtr Name;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetSource(IntPtr sender);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateReceive(ref ReceiveCreate settings);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Capture(IntPtr receiver, ref NdiRuntime.VideoFrame video, IntPtr audio, IntPtr metadata, uint timeout);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FreeVideo(IntPtr receiver, ref NdiRuntime.VideoFrame video);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DestroyReceive(IntPtr receiver);

    public static void Lighting()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        byte[] image = [0,0,255,255, 0,255,0,255, 255,0,0,255, 10,10,10,255];
        var settings = new NdiSettings { Width = 2, Height = 2, BlackCutoff = 0 };
        Check(LightingOutput.Process(image, 2, 2, settings).SequenceEqual(image), "Lighting pass changed identity BGRA or flipped rows.");
        var cutoff = LightingOutput.Process(image, 2, 2, settings with { BlackCutoff = 0.05f });
        Check(cutoff[12] == 0 && cutoff[13] == 0 && cutoff[14] == 0 && cutoff[15] == 255, "Black cutoff did not produce an opaque blackout.");
        var red = LightingOutput.Process(image, 2, 2, settings with { Channel = 2, Alpha = 1 });
        Check(red[0] == 255 && red[1] == 255 && red[2] == 255 && red[3] == 255 && red[4] == 0 && red[7] == 0, "Red routing or luma alpha is wrong.");
        var inverted = LightingOutput.Process(image, 2, 2, settings with { Channel = 2, Alpha = 2 });
        Check(inverted[3] == 0 && inverted[7] == 255, "Inverted alpha is wrong.");
        var enlarged = LightingOutput.Process(image, 2, 2, settings with { Width = 4, Height = 4 });
        Check(enlarged[2] == 255 && enlarged[(3*4)*4] == 255, "Resizing changed frame orientation.");
        Check(Marshal.SizeOf<NdiRuntime.VideoFrame>() == 72 && Marshal.OffsetOf<NdiRuntime.VideoFrame>(nameof(NdiRuntime.VideoFrame.Data)).ToInt32() == 40, "NDI video ABI layout is wrong.");
        Console.WriteLine("Lighting calibration and NDI ABI checks passed.");
    }

    public static void RoundTrip(string path)
    {
        var runtime = NdiRuntime.Load(path);
        using var sender = new NdiSender(runtime, "LZX-V Native ABI Test " + Guid.NewGuid().ToString("N")[..8]);
        var source = Marshal.PtrToStructure<Source>(runtime.Export<GetSource>("NDIlib_send_get_source_name")(sender.Handle));
        var create = new ReceiveCreate { Source = source, ColorFormat = 0, Bandwidth = 100 };
        IntPtr receiver = runtime.Export<CreateReceive>("NDIlib_recv_create_v3")(ref create);
        if (receiver == IntPtr.Zero) throw new Exception("NDI test receiver could not be created.");
        try
        {
            var capture = runtime.Export<Capture>("NDIlib_recv_capture_v2");
            var free = runtime.Export<FreeVideo>("NDIlib_recv_free_video_v2");
            byte[] pixels = new byte[64*36*4];
            for (int y = 0; y < 36; y++) for (int x = 0; x < 64; x++)
            { int i = (y*64+x)*4; pixels[i + (y < 18 ? 2 : 0)] = 255; pixels[i+3] = 255; }
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < 8)
            {
                sender.Send(pixels, 64, 36, 30);
                var frame = new NdiRuntime.VideoFrame();
                if (capture(receiver, ref frame, IntPtr.Zero, IntPtr.Zero, 30) != 1) continue;
                try
                {
                    if (frame.Width != 64 || frame.Height != 36) throw new Exception("Received NDI dimensions are wrong.");
                    var top = new byte[4]; var bottom = new byte[4];
                    Marshal.Copy(frame.Data + frame.Stride*9 + 32*4, top, 0, 4);
                    Marshal.Copy(frame.Data + frame.Stride*27 + 32*4, bottom, 0, 4);
                    if (top[2] < 180 || top[0] > 80 || bottom[0] < 180 || bottom[2] > 80)
                        throw new Exception("NDI round trip changed BGRA channels or vertical orientation.");
                    Console.WriteLine("Real NDI sender/receiver round trip passed (64×36, top red / bottom blue).");
                    return;
                }
                finally { free(receiver, ref frame); }
            }
            throw new Exception("No NDI video frame was received before the timeout.");
        }
        finally { runtime.Export<DestroyReceive>("NDIlib_recv_destroy")(receiver); }
    }
}
