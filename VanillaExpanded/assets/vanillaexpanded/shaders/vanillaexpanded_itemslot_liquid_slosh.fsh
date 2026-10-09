#version 330 core

uniform vec4 color;
layout(location = 0) out vec4 fragColor;

/* Retains the provider's color and opacity with exactly one premultiplication. */
void main()
{
    fragColor = vec4(color.rgb * color.a, color.a);
}
