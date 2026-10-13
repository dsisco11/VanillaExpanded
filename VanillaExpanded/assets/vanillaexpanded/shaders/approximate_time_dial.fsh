#version 330 core
in vec2 scaleUv;
uniform sampler2D shapeMask;
uniform float opacity;
uniform float rotation;
out vec4 outColor;
/** Rotates the scenery behind a fixed upper semicircular aperture. */
void main()
{
    vec2 point = scaleUv - vec2(0.5);
    if (point.y > 0.0 || length(point) > 0.35) discard;
    float c = cos(rotation), s = sin(rotation);
    vec2 uv = mat2(c, -s, s, c) * point / 0.80 + vec2(0.5);
    vec4 color = texture(shapeMask, uv);
    outColor = vec4(color.rgb, color.a * opacity);
}
