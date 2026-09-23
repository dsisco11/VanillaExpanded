#version 330 core

in vec2 radialPosition;
in float wedgeFraction;
flat in int entryIndex;
out vec4 fragColor;

uniform vec4 entryStates[64];
uniform int entryCount;
uniform int hoveredIndex;
uniform float centerRadius;
uniform float innerRadius;
uniform float outerRadius;
uniform float separatorFraction;
uniform float animationTime;

/* Shades one mesh-defined entry using supplied state and analytic edge coverage. */
void main()
{
    if (entryIndex < 0 || entryIndex >= entryCount) discard;
    float radius = length(radialPosition);
    float radialAA = max(fwidth(radius), 0.00001);
    bool center = entryIndex == entryCount - 1;
    float coverage = center
        ? 1.0 - smoothstep(centerRadius - radialAA, centerRadius + radialAA, radius)
        : smoothstep(innerRadius - radialAA, innerRadius + radialAA, radius)
            * (1.0 - smoothstep(outerRadius - radialAA, outerRadius + radialAA, radius));

    if (!center)
    {
        // Geometry defines membership; interpolated edge distance softens only its separators.
        float angularAA = max(fwidth(wedgeFraction), 0.00001);
        coverage *= smoothstep(separatorFraction - angularAA, separatorFraction + angularAA, wedgeFraction);
        coverage *= 1.0 - smoothstep(1.0 - separatorFraction - angularAA, 1.0 - separatorFraction + angularAA, wedgeFraction);
    }

    vec4 state = entryStates[entryIndex];
    float enabled = state.x;
    float selected = state.y;
    float hovered = entryIndex == hoveredIndex ? 1.0 : 0.0;
    vec3 baseColor = mix(vec3(0.15, 0.16, 0.19), vec3(0.25, 0.29, 0.36), enabled);
    float pulse = 0.5 + 0.5 * sin(animationTime * 4.0);
    vec3 color = mix(baseColor, vec3(0.60, 0.73, 0.91) + pulse * 0.07, hovered * enabled);
    color = mix(color, vec3(0.86, 0.77, 0.48) + pulse * 0.09, selected);
    float edge = center ? abs(radius - centerRadius) : min(abs(radius - innerRadius), abs(radius - outerRadius));
    color += (1.0 - smoothstep(0.0, radialAA * 3.0, edge)) * vec3(0.10);
    fragColor = vec4(color, coverage * mix(0.50, 0.90, enabled));
}


