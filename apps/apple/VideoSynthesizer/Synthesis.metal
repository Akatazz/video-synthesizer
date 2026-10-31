#include <metal_stdlib>
using namespace metal;

struct Uniforms {
    float time, width, height, oscFreq, oscSpeed, zoom, detail, morph;
    float feed, kill, feedback, colorShift;
};
constexpr sampler linearClamp(coord::normalized, address::clamp_to_edge, filter::linear);

kernel void synthesize(texture2d<float, access::sample> history [[texture(0)]],
                       texture2d<half, access::write> output [[texture(1)]],
                       constant Uniforms &p [[buffer(0)]], uint2 position [[thread_position_in_grid]]) {
    if (position.x >= output.get_width() || position.y >= output.get_height()) return;
    float2 uv = (float2(position) + 0.5) / float2(p.width, p.height);
    float2 texel = 1.0 / float2(p.width, p.height);
    float2 warp = float2(sin(uv.y * p.oscFreq + p.time * p.oscSpeed),
                         cos(uv.x * p.oscFreq + p.time * p.oscSpeed)) * 0.01;
    float2 z = (uv + warp - 0.5) * p.zoom * 5.0;
    float iterations = 0;
    for (int i = 0; i < 30 && float(i) < p.detail; ++i) {
        z = float2(z.x*z.x - z.y*z.y, 2*z.x*z.y) + float2(p.morph, 0.156 + sin(p.time*0.1)*0.1);
        if (dot(z,z) > 4.0) break;
        iterations += 1;
    }
    float smooth = iterations - log2(max(1.0, log2(max(dot(z,z), 1e-10))));
    float value = smooth / p.detail;
    float3 fractal = float3(value, value * 0.5, sin(value * 3.14)*0.5 + 0.5);
    float3 laplacian = -history.sample(linearClamp, uv).rgb;
    for (int y = -1; y <= 1; ++y) {
        for (int x = -1; x <= 1; ++x) {
            if (x == 0 && y == 0) continue;
            float weight = x == 0 || y == 0 ? 0.2 : 0.05;
            laplacian += history.sample(linearClamp, uv + float2(x,y)*texel).rgb * weight;
        }
    }
    float3 last = history.sample(linearClamp, uv + warp*0.5).rgb;
    float3 reaction = last + laplacian - last*last*last + p.feed*(1.0-last) - p.kill*last;
    float3 palette = 0.5 + 0.5*cos(6.28318*(reaction.r + p.colorShift + float3(0, 0.33, 0.67)));
    float3 composite = mix(palette*reaction, fractal, 0.05);
    output.write(half4(half3(clamp(mix(fractal, composite, p.feedback), 0.0, 1.0)), 1), position);
}

struct ScreenVertex { float4 position [[position]]; float2 uv; };
vertex ScreenVertex screenVertex(uint id [[vertex_id]]) {
    float2 positions[3] = { float2(-1,-1), float2(3,-1), float2(-1,3) };
    ScreenVertex result;
    result.position = float4(positions[id], 0, 1);
    result.uv = float2(positions[id].x * 0.5 + 0.5, 0.5 - positions[id].y * 0.5);
    return result;
}
fragment half4 screenFragment(ScreenVertex input [[stage_in]], texture2d<half> image [[texture(0)]]) {
    return image.sample(linearClamp, input.uv);
}
