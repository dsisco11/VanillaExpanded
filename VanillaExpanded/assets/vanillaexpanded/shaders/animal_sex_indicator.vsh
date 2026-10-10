#version 330 core

layout(location = 0) in vec3 vertexPositionIn;
layout(location = 1) in vec2 uvIn;

uniform mat4 projectionMatrix;
uniform mat4 viewMatrix;
uniform mat4 modelMatrix;
out vec2 uv;

void main()
{
    uv = uvIn;
    gl_Position = projectionMatrix * viewMatrix * modelMatrix * vec4(vertexPositionIn, 1.0);
}
