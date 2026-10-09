#version 330 core

layout(location = 0) out vec4 fragColor;

/** Supplies the engine-required fragment stage; simulation executes with rasterizer discard enabled. */
void main()
{
    fragColor = vec4(0.0);
}
