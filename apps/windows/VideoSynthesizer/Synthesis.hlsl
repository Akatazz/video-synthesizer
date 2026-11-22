// Independently compiled Direct3D 11 compute implementation. UVs and BGRA bytes
// are top-down, matching the native preset/renderer and NDI contracts.
cbuffer Parameters : register(b0) {
    float time, width, height, oscFreq;
    float oscSpeed, zoom, detail, morph;
    float feed, kill, feedback, colorShift;
    float audioToOsc, audioToFractal, audioToCA, bass;
    float mid, high, padding0, padding1;
};
Texture2D<float4> previousFrame : register(t0);
SamplerState linearClamp : register(s0);
RWTexture2D<float4> nextFrame : register(u0);
RWTexture2D<float4> outputBytes : register(u1);

float3 sampleHistory(float2 uv) {
    return previousFrame.SampleLevel(linearClamp,uv,0).rgb;
}

[numthreads(8,8,1)]
void synthesize(uint3 pixel : SV_DispatchThreadID) {
    if (pixel.x >= (uint)width || pixel.y >= (uint)height) return;
    float2 uv = (float2(pixel.xy)+0.5)/float2(width,height);
    float2 texel = 1.0/float2(width,height);
    float speed = oscSpeed+mid*audioToOsc*5.0;
    float2 warp = float2(sin(uv.y*oscFreq+time*speed),cos(uv.x*oscFreq+time*speed))*0.01;
    float2 z = (uv+warp-0.5)*zoom*5.0;
    float2 c = float2(morph+bass*audioToFractal*0.5,0.156+sin(time*0.1)*0.1);
    float iterations = 0;
    [loop] for (int i=0; i<30 && i<detail; ++i) {
        z = float2(z.x*z.x-z.y*z.y,2*z.x*z.y)+c;
        if (dot(z,z)>4) break;
        iterations++;
    }
    float value = (iterations-log2(max(1.0,log2(max(dot(z,z),1e-10)))))/detail;
    float3 fractal = float3(value,value*0.5,sin(value*3.14)*0.5+0.5);
    float3 laplacian = -sampleHistory(uv);
    [unroll] for (int dy=-1; dy<=1; ++dy) {
        [unroll] for (int dx=-1; dx<=1; ++dx) {
            if (dx!=0 || dy!=0)
                laplacian += sampleHistory(uv+float2(dx,dy)*texel)*(dx==0 || dy==0 ? 0.2 : 0.05);
        }
    }
    float3 last = sampleHistory(uv+warp*0.5);
    float3 reaction = last+laplacian-last*last*last+(feed+high*audioToCA*0.05)*(1-last)-kill*last;
    float3 palette = 0.5+0.5*cos(6.28318*(reaction.r+colorShift+float3(0,0.33,0.67)));
    float3 result = saturate(lerp(fractal,lerp(palette*reaction,fractal,0.05),feedback));
    nextFrame[pixel.xy] = float4(result,1);
    // RGBA UAV storage, explicitly swizzled to the BGRA byte order used by WPF.
    outputBytes[pixel.xy] = float4(result.bgr,1);
}
