#version 330 core
in vec2 frameUv;
out vec4 outColor;
uniform vec2 frameSize;
uniform float opacity;
void main()
{
    vec2 p = frameUv * frameSize;
    vec2 edge = min(p, frameSize - p);
    // Select the nearest edge and measure clockwise around the perimeter. One quad
    // covers the entire frame; the fragment mask supplies both its stroke and gaps.
    float along;
    if (edge.y <= edge.x)
        along = p.y < frameSize.y * 0.5 ? p.x : frameSize.x + frameSize.y + frameSize.x - p.x;
    else
        along = p.x > frameSize.x * 0.5 ? frameSize.x + p.y : 2.0 * frameSize.x + frameSize.y + frameSize.y - p.y;
    float coverage = 1.0 - smoothstep(0.75, 1.25, min(edge.x, edge.y));
    float dash = 1.0 - smoothstep(3.5, 4.5, mod(along, 7.0));
    outColor = vec4(vec3(150.0 / 255.0), opacity * coverage * dash);
}
