#version 330 core

layout(location = 0) in vec3 vertex;
layout(location = 1) in vec2 uv;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;

out vec2 radialPosition;
out float wedgeFraction;
flat out int entryIndex;

/* Transforms fixed menu geometry while retaining its per-entry and edge attributes. */
void main()
{
    radialPosition = vertex.xy;
    wedgeFraction = uv.y;
    entryIndex = int(uv.x + 0.5);
    gl_Position = projectionMatrix * modelViewMatrix * vec4(vertex, 1.0);
}

