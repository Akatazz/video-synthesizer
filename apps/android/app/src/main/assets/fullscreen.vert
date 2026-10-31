#version 300 es
precision highp float;
out vec2 uv;
void main() {
    vec2 positions[3] = vec2[3](vec2(-1,-1), vec2(3,-1), vec2(-1,3));
    vec2 position = positions[gl_VertexID];
    gl_Position = vec4(position, 0, 1);
    uv = position * 0.5 + 0.5;
}
