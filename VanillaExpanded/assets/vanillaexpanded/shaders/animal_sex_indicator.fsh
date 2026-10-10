#version 330 core

uniform sampler2D iconTexture;
uniform vec4 iconTint;
in vec2 uv;
layout(location = 0) out vec4 outColor;

void main()
{
    outColor = texture(iconTexture, uv) * iconTint;
    if (outColor.a < 0.001) discard;
}
