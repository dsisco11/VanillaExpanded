#version 330 core

layout(location = 0) in vec3 vertex;

out vec2 vPos;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;

void main()
{
    gl_Position = projectionMatrix * modelViewMatrix * vec4(vertex, 1.0);
    // Pass vertex xy directly (already in [-1,1] range)
    vPos = vertex.xy;
}
