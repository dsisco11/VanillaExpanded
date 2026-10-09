#version 330 core
uniform vec4 color;
in float surfaceDepth;
layout(location = 0) out vec4 fragColor;

/** Retains the metal's heat tint and straight-alpha input without double premultiplication. */
void main()
{
    float rim = 1.0 - smoothstep(0.0, 0.018, surfaceDepth);
    vec3 glow = mix(color.rgb * 0.85, min(vec3(1.0), color.rgb + vec3(0.25, 0.2, 0.12)), rim);
    fragColor = vec4(glow * color.a, color.a);
}
