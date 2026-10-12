#version 330 core
in vec2 backdropUv;
out vec4 outColor;
void main()
{
    // Native quad coordinates span [-1, 1]; smoothstep gives a soft circular edge with no texture sampling.
    float radius = length(backdropUv);
    float alpha = 0.22 * (1.0 - smoothstep(0.0, 1.0, radius));
    outColor = vec4(0.0, 0.0, 0.0, alpha);
}
