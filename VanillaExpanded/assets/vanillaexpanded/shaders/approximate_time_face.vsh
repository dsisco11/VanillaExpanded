#version 330 core
layout(location = 0) in vec3 vertex;

/* Covers the owned framebuffer without using the surrounding GUI matrices. */
void main()
{
    gl_Position = vec4(vertex.xy, 0.0, 1.0);
}
