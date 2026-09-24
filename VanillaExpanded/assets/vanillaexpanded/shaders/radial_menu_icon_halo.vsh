#version 330 core
layout(location = 0) in vec3 vertex;

/* Covers the destination viewport without altering the icon's GUI projection. */
void main()
{
    gl_Position = vec4(vertex.xy, 0.0, 1.0);
}
