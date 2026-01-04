#version 330 core

layout(location = 0) in vec3 vertex;

out vec2 vUV;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;

void main()
{
    gl_Position = projectionMatrix * modelViewMatrix * vec4(vertex, 1.0);
    // Map vertex xy from [-1,1] to UV [0,1]
    vUV = (vertex.xy + 1.0) * 0.5;
}
