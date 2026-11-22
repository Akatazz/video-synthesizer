using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Buffer = Vortice.Direct3D11.ID3D11Buffer;

namespace VideoSynthesizer;

/// <summary>Native float ping-pong compute synthesis with bounded BGRA readback.</summary>
public sealed class Direct3DSynthRenderer : ISynthRenderer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public string Backend { get; }
    private readonly List<IDisposable> resources = [];
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly ID3D11ComputeShader shader;
    private readonly Buffer constants;
    private readonly ID3D11SamplerState sampler;
    private readonly ID3D11Texture2D[] history = new ID3D11Texture2D[2];
    private readonly ID3D11ShaderResourceView[] reads = new ID3D11ShaderResourceView[2];
    private readonly ID3D11UnorderedAccessView[] writes = new ID3D11UnorderedAccessView[2];
    private readonly ID3D11Texture2D output, staging;
    private readonly ID3D11UnorderedAccessView outputView;
    private int readIndex;
    private bool disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct Uniforms {
        public Vector4 Frame, Oscillation, Reaction, Routes, Bands;
    }

    public Direct3DSynthRenderer(int width, int height, bool software = false)
    {
        if (width < 1 || height < 1 || width > 1920 || height > 1080)
            throw new ArgumentOutOfRangeException(nameof(width));
        Width = width; Height = height; Pixels = new byte[width*height*4];
        Backend = software ? "Direct3D 11 · WARP software" : "Direct3D 11 · GPU";
        try {
            D3D11.D3D11CreateDevice((IDXGIAdapter?)null,software ? DriverType.Warp : DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,[FeatureLevel.Level_11_0],out device,out context).CheckError();
            Own(device); Own(context);
            using var source = new StreamReader(typeof(Direct3DSynthRenderer).Assembly.GetManifestResourceStream("Synthesis.hlsl")
                ?? throw new InvalidDataException("The embedded Direct3D shader is missing."));
            var bytecode = Compiler.Compile(source.ReadToEnd(),"synthesize","Synthesis.hlsl","cs_5_0",ShaderFlags.OptimizationLevel3);
            shader = Own(device.CreateComputeShader(bytecode.Span));
            constants = Own(device.CreateBuffer(new BufferDescription(80,BindFlags.ConstantBuffer)));
            sampler = Own(device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear,
                TextureAddressMode.Clamp,TextureAddressMode.Clamp,TextureAddressMode.Clamp)));
            var description = new Texture2DDescription(Format.R32G32B32A32_Float,(uint)width,(uint)height,
                mipLevels:1,bindFlags:BindFlags.ShaderResource|BindFlags.UnorderedAccess);
            for (int i=0;i<2;i++) {
                history[i] = Own(device.CreateTexture2D(description));
                reads[i] = Own(device.CreateShaderResourceView(history[i]));
                writes[i] = Own(device.CreateUnorderedAccessView(history[i]));
            }
            output = Own(device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm,(uint)width,(uint)height,
                mipLevels:1,bindFlags:BindFlags.UnorderedAccess)));
            outputView = Own(device.CreateUnorderedAccessView(output));
            staging = Own(device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm,(uint)width,(uint)height,
                mipLevels:1,bindFlags:BindFlags.None,usage:ResourceUsage.Staging,cpuAccessFlags:CpuAccessFlags.Read)));
            Reset();
        } catch { Dispose(); throw; }
    }

    private T Own<T>(T resource) where T : IDisposable { resources.Add(resource); return resource; }

    public void Reset() {
        ObjectDisposedException.ThrowIf(disposed,this);
        context.ClearState();
        foreach (var view in writes) context.ClearUnorderedAccessView(view,new Color4(0,0,0,0));
        readIndex = 0;
        Array.Clear(Pixels);
    }

    public void Render(float time, SynthParameters p, AudioBands audio = default) {
        ObjectDisposedException.ThrowIf(disposed,this);
        p.Validate();
        if (!float.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        var uniforms = new Uniforms {
            Frame = new(time,Width,Height,p.OscFreq),
            Oscillation = new(p.OscSpeed,p.FractalZoom,p.FractalDetail,p.FractalMorph),
            Reaction = new(p.CaFeed,p.CaKill,p.FeedbackMix,p.ColorShift),
            Routes = new(p.AudioToOsc,p.AudioToFractal,p.AudioToCA,audio.Bass),
            Bands = new(audio.Mid,audio.High,0,0)
        };
        context.UpdateSubresource(uniforms,constants);
        context.CSSetShader(shader);
        context.CSSetConstantBuffer(0,constants);
        context.CSSetSampler(0,sampler);
        context.CSSetShaderResource(0,reads[readIndex]);
        context.CSSetUnorderedAccessView(0,writes[1-readIndex]);
        context.CSSetUnorderedAccessView(1,outputView);
        context.Dispatch((uint)(Width+7)/8,(uint)(Height+7)/8,1);
        context.CSSetShaderResource(0,null);
        context.CSSetUnorderedAccessView(0,null);
        context.CSSetUnorderedAccessView(1,null);
        context.CopyResource(staging,output);
        var mapped = context.Map(staging,0,MapMode.Read);
        try {
            for (int row=0;row<Height;row++)
                Marshal.Copy(mapped.DataPointer+(int)(row*mapped.RowPitch),Pixels,row*Width*4,Width*4);
        } finally { context.Unmap(staging,0); }
        readIndex = 1-readIndex;
    }

    public void Dispose() {
        if (disposed) return;
        disposed = true;
        context?.ClearState();
        for (int i=resources.Count-1;i>=0;i--) resources[i].Dispose();
        resources.Clear();
    }
}
