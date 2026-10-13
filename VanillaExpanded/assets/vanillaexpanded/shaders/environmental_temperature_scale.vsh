#version 330 core
layout(location = 0) in vec3 vertex;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
out vec2 scaleUv;
/** Maps the shared quad into the host GUI transform and normalized mask coordinates. */
void main()
{
    scaleUv = vertex.xy * 0.5 + 0.5;
    gl_Position = projectionMatrix * modelViewMatrix * vec4(vertex, 1.0);
}
