#version 330 core

layout(location = 0) in vec3 vertex;
layout(location = 1) in vec2 uv;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform int entryCount;
uniform vec4 entryStates[64];
uniform float hoverScale;

out vec2 radialPosition;
out float wedgeFraction;
flat out int entryIndex;

/* Transforms fixed menu geometry while retaining its per-entry and edge attributes. */
void main()
{
    radialPosition = vertex.xy;
    wedgeFraction = uv.y;
    entryIndex = int(uv.x + 0.5);
    // Stable entry IDs select an interpolated scale; the underlying mesh remains cached.
    float scale = entryIndex == entryCount - 1 ? 1.0 : mix(1.0, hoverScale, entryStates[entryIndex].z);
    gl_Position = projectionMatrix * modelViewMatrix * vec4(vertex.xy * scale, vertex.z, 1.0);
}

