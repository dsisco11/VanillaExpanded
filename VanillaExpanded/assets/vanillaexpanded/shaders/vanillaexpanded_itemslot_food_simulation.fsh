#version 330 core
layout(location=0) out vec4 fragColor;
/* Rasterization is discarded during simulation. */
void main() { fragColor = vec4(0.0); }
