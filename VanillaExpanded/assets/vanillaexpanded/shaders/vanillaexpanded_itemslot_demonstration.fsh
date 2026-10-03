#version 330 core

uniform vec4 color;
layout(location = 0) out vec4 outColor;

/* Preserves provider color and opacity, premultiplying exactly once for the shared blend contract. */
void main()
{
    outColor = vec4(color.rgb * color.a, color.a);
}
