#version 330 core
in vec2 scaleUv;
uniform sampler2D shapeMask;
uniform float opacity;
uniform float rotation;
uniform float temperatureFill;
out vec4 outColor;
/** Rotates the scenery behind a fixed upper semicircular aperture. */
void main()
{
    if (temperatureFill >= 0.0)
    {
        // Authored glass follows an elliptical left rim; liquid rises from the bulb along that arc.
        vec2 p = (scaleUv - vec2(0.5, 0.49)) / vec2(0.39, 0.37);
        float angle = atan(p.y, p.x);
        if (angle < 0.0) angle += 6.2831853;
        float endAngle = mix(2.4, 4.05, temperatureFill);
        float tube = abs(length(p) - 1.0) * 0.38 - 0.009;
        float aa = max(fwidth(tube), 0.0001);
        float coverage = (1.0 - smoothstep(-aa, aa, tube))
            * step(2.4, angle) * (1.0 - smoothstep(endAngle - fwidth(angle), endAngle + fwidth(angle), angle));
        float bulb = length(scaleUv - vec2(0.218, 0.746)) - 0.033;
        coverage = max(coverage, 1.0 - smoothstep(-fwidth(bulb), fwidth(bulb), bulb));
        outColor = vec4(mix(vec3(0.72, 0.015, 0.01), vec3(1.0, 0.12, 0.06),
            clamp(0.5 - (length(p) - 1.0) * 25.0, 0.0, 1.0)), coverage * opacity);
        return;
    }
    vec2 point = scaleUv - vec2(0.5);
    if (point.y > 0.0 || length(point) > 0.35) discard;
    float c = cos(rotation), s = sin(rotation);
    vec2 uv = mat2(c, -s, s, c) * point / 0.80 + vec2(0.5);
    vec4 color = texture(shapeMask, uv);
    outColor = vec4(color.rgb, color.a * opacity);
}
