#version 330 core

layout(location = 0) in vec3 vertexPositionIn;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform vec4 tileBounds;
out vec2 uv;

void main()
{
    uv = vertexPositionIn.xy;
    vec2 position = tileBounds.xy + uv * tileBounds.zw;
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 220.0, 1.0);
}
