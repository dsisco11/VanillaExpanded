#version 330 core
uniform sampler2D shapeMask;
uniform float opacity;
in vec2 scaleUv;
out vec4 outColor;
/** Colors the scale from its vertical coordinate, with hot at the top; the texture supplies only edge coverage. */
void main()
{
    float position = clamp(1.0 - scaleUv.y, 0.0, 1.0);
    vec3 cold = vec3(0.35, 0.75, 1.0);
    vec3 neutral = vec3(0.82, 0.84, 0.84);
    vec3 hot = vec3(1.0, 0.62, 0.18);
    vec3 color = position <= 0.5
        ? mix(cold, neutral, position * 2.0)
        : mix(neutral, hot, (position - 0.5) * 2.0);
    outColor = vec4(color, texture(shapeMask, vec2(1.0 - scaleUv.y, scaleUv.x)).a * opacity);
}
