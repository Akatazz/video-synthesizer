#version 300 es
precision highp float;
uniform sampler2D history;
// time, width, height, followed by the nine preset values in contract order.
uniform float p[18];
out vec4 color;
void main() {
    vec2 size = vec2(p[1], p[2]);
    // Logical synthesis coordinates have a top-left origin. Texture lookup
    // converts back to OpenGL's bottom-left storage convention.
    vec2 uv = vec2(gl_FragCoord.x / size.x, 1.0 - gl_FragCoord.y / size.y);
    vec2 texel = 1.0 / size;
    float speed = p[4] + p[16]*p[12]*5.0;
    float morph = p[7] + p[15]*p[13]*0.5;
    float feed = p[8] + p[17]*p[14]*0.05;
    vec2 warp = vec2(sin(uv.y*p[3] + p[0]*speed), cos(uv.x*p[3] + p[0]*speed)) * 0.01;
    vec2 z = (uv + warp - 0.5) * p[5] * 5.0;
    float iterations = 0.0;
    for (int i=0; i<30; ++i) {
        if (float(i) >= p[6]) break;
        z = vec2(z.x*z.x-z.y*z.y, 2.0*z.x*z.y) + vec2(morph, 0.156+sin(p[0]*0.1)*0.1);
        if (dot(z,z)>4.0) break;
        iterations += 1.0;
    }
    float smoothValue = iterations-log2(max(1.0,log2(max(dot(z,z),1e-10))));
    float value = smoothValue/p[6];
    vec3 fractal = vec3(value,value*0.5,sin(value*3.14)*0.5+0.5);
    vec3 laplacian = -texture(history, vec2(uv.x,1.0-uv.y)).rgb;
    for (int y=-1; y<=1; ++y) for (int x=-1; x<=1; ++x) {
        if (x==0 && y==0) continue;
        vec2 sampleUV = uv+vec2(x,y)*texel;
        laplacian += texture(history,vec2(sampleUV.x,1.0-sampleUV.y)).rgb * (x==0 || y==0 ? 0.2 : 0.05);
    }
    vec2 displaced = uv+warp*0.5;
    vec3 last = texture(history,vec2(displaced.x,1.0-displaced.y)).rgb;
    vec3 reaction = last+laplacian-last*last*last+feed*(1.0-last)-p[9]*last;
    vec3 palette = 0.5+0.5*cos(6.28318*(reaction.r+p[11]+vec3(0,0.33,0.67)));
    vec3 composite = mix(palette*reaction,fractal,0.05);
    color = vec4(clamp(mix(fractal,composite,p[10]),0.0,1.0),1.0);
}
