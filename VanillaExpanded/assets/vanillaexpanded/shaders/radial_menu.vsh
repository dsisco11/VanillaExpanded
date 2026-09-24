#version 330 core

layout(location = 0) in vec3 vertex;
layout(location = 1) in vec2 uv;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform int hoveredIndex;
uniform int entryCount;

out vec2 radialPosition;
out float wedgeFraction;
flat out int entryIndex;

/* Transforms fixed menu geometry while retaining its per-entry and edge attributes. */
void main()
{
    radialPosition = vertex.xy;
    wedgeFraction = uv.y;
    entryIndex = int(uv.x + 0.5);
    // Grow only the hovered outer wedge; the center and other wedges remain fixed.
    float scale = entryIndex == hoveredIndex && entryIndex != entryCount - 1 ? 1.15 : 1.0;
    gl_Position = projectionMatrix * modelViewMatrix * vec4(vertex.xy * scale, vertex.z, 1.0);
}

