#version 330 core
layout(location = 0) in vec3 vertex;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
out vec2 backdropUv;
void main()
{
    // Preserve the local radius coordinates while positioning the quad through the enclosing GUI matrices.
    backdropUv = vertex.xy;
    gl_Position = projectionMatrix * modelViewMatrix * vec4(vertex, 1.0);
}
