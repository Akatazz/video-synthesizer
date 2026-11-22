using System.Diagnostics;
using VideoSynthesizer;

internal static class GraphicsChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }

    public static void Run(SynthParameters preset)
    {
        // Non-group-aligned dimensions also exercise dispatch bounds and row pitch.
        using var gpu = new Direct3DSynthRenderer(65,37,software:true);
        using var cpu = new SynthEngine(65,37);
        gpu.Render(1,preset); cpu.Render(1,preset);
        var first = gpu.Pixels.ToArray();
        Compare(first,cpu.Pixels,"clean frame",0.8);
        Require(first.Where((_,i)=>i%4==3).All(x=>x==255),"Direct3D output must be opaque BGRA.");
        Require(first.Where((_,i)=>i%4!=3).Distinct().Count()>30,"Direct3D synthesis is blank/constant.");
        for (int frame=0;frame<5;frame++) { gpu.Render(1,preset); cpu.Render(1,preset); }
        Require(!first.SequenceEqual(gpu.Pixels),"Direct3D feedback does not evolve.");
        Compare(gpu.Pixels,cpu.Pixels,"recursive feedback",2.5);
        gpu.Reset(); gpu.Render(1,preset);
        Require(first.SequenceEqual(gpu.Pixels),"Direct3D reset must clear both history textures.");
        gpu.Reset(); gpu.Render(1,preset with { FractalMorph=-0.8f,ColorShift=0.4f });
        Require(!first.SequenceEqual(gpu.Pixels),"Direct3D controls do not change output.");
        var dry = preset with { FeedbackMix=0 };
        gpu.Reset(); gpu.Render(1,dry); var dryFrame = gpu.Pixels.ToArray();
        gpu.Render(1,dry);
        Require(dryFrame.SequenceEqual(gpu.Pixels),"Dry GPU synthesis depends on feedback history.");
        var routesOff = preset with { AudioToOsc=0,AudioToFractal=0,AudioToCA=0 };
        gpu.Reset(); gpu.Render(1,routesOff); var noAudio = gpu.Pixels.ToArray();
        gpu.Reset(); gpu.Render(1,routesOff,new AudioBands(1,1,1));
        Require(noAudio.SequenceEqual(gpu.Pixels),"Zero GPU routes must bypass audio.");
        gpu.Reset(); gpu.Render(1,preset,new AudioBands(0.8f,0.6f,0.4f));
        cpu.Reset(); cpu.Render(1,preset,new AudioBands(0.8f,0.6f,0.4f));
        Require(!first.SequenceEqual(gpu.Pixels),"Audio does not modulate the GPU shader.");
        Compare(gpu.Pixels,cpu.Pixels,"audio modulation",0.8);
        bool rejected = false;
        try { gpu.Render(float.NaN,preset); } catch (ArgumentOutOfRangeException) { rejected=true; }
        Require(rejected,"GPU renderer accepted non-finite time.");

        using (var hd = new Direct3DSynthRenderer(1920,1080,software:true)) {
            var clock = Stopwatch.StartNew();
            hd.Render(1,preset);
            Require(hd.Pixels.Length==1920*1080*4 && hd.Pixels[^1]==255,"Full-HD GPU readback is incomplete.");
            Require(hd.Pixels.Where((_,i)=>i%4!=3).Distinct().Count()>30,"Full-HD GPU output is blank.");
            Require(hd.Pixels.Where((_,i)=>i%4==3).All(x=>x==255),"Full-HD GPU dispatch left unwritten pixels.");
            Console.WriteLine($"Direct3D WARP 1920×1080 synthesis/readback: {clock.Elapsed.TotalMilliseconds:0.0} ms (software validation, not GPU throughput).");
        }
        gpu.Dispose(); gpu.Dispose();
        rejected = false;
        try { gpu.Render(1,preset); } catch (ObjectDisposedException) { rejected=true; }
        Require(rejected,"Disposed GPU renderer still accepts frames.");
        Console.WriteLine("Direct3D WARP synthesis, CPU parity, feedback/reset, audio, full-HD readback and teardown passed.");

        Direct3DSynthRenderer hardware;
        try { hardware = new Direct3DSynthRenderer(1280,720); }
        catch (Exception error) { Console.WriteLine($"SKIP hardware-only timing: {error.Message}"); return; }
        using (hardware) {
            hardware.Render(0,preset); // Warm up shader/device before timing.
            var clock = Stopwatch.StartNew();
            for (int frame=0;frame<10;frame++) hardware.Render(frame/30f,preset);
            Console.WriteLine($"Hardware Direct3D 1280×720 synthesis/readback: {clock.Elapsed.TotalMilliseconds/10:0.0} ms/frame (10-frame sample).");
        }
    }

    private static void Compare(byte[] gpu, byte[] cpu, string context, double meanLimit)
    {
        double sum=0; int count=0,large=0;
        for (int i=0;i<gpu.Length;i++) {
            if (i%4==3) continue;
            int difference=Math.Abs(gpu[i]-cpu[i]);
            sum+=difference; count++; if (difference>8) large++;
        }
        Console.WriteLine($"GPU/CPU {context}: mean byte error {sum/count:0.000}, >8-byte errors {100.0*large/count:0.00}%.");
        Require(sum/count<meanLimit && large<count*0.02,$"GPU/CPU pixels diverged for {context}; check channel order, UV origin, uniforms and history.");
    }
}
