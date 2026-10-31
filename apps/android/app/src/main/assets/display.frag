#version 300 es
precision highp float;
in vec2 uv;
uniform sampler2D history;
out vec4 color;
void main() { color = texture(history, uv); }
