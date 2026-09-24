#version 330 core

in vec2 radialPosition;
in float wedgeFraction;
flat in int entryIndex;
out vec4 fragColor;

uniform vec4 entryStates[64];
uniform int entryCount;
uniform int hoveredIndex;
uniform int maskIndex;
uniform float centerRadius;
uniform float innerRadius;
uniform float outerRadius;
uniform float separatorFraction;
uniform float startAngleRadians;
uniform float clockwiseSign;
uniform float radiusPixels;
uniform float cornerRadiusPixels;
uniform float borderWidthPixels;
uniform float hoverScale;
uniform float enabledOpacity;
uniform float disabledOpacity;
uniform float grainStrength;
uniform vec3 disabledFill;
uniform vec3 enabledFill;
uniform vec3 hoverFill;
uniform vec3 selectedFill;
uniform vec3 borderColor;
uniform vec3 hoverBorderColor;

/* Produces stable, gently filtered grain in the wedge's unscaled local coordinates. */
float grain(vec2 position)
{
    vec2 cell = floor(position);
    vec2 fraction = fract(position);
    fraction = fraction * fraction * (3.0 - 2.0 * fraction);
    float a = fract(sin(dot(cell, vec2(127.1, 311.7))) * 43758.5453);
    float b = fract(sin(dot(cell + vec2(1.0, 0.0), vec2(127.1, 311.7))) * 43758.5453);
    float c = fract(sin(dot(cell + vec2(0.0, 1.0), vec2(127.1, 311.7))) * 43758.5453);
    float d = fract(sin(dot(cell + vec2(1.0, 1.0), vec2(127.1, 311.7))) * 43758.5453);
    return mix(mix(a, b, fraction.x), mix(c, d, fraction.x), fraction.y) - 0.5;
}

/* Measures the rounded annular sector in screen pixels so borders and corners share one contour. */
float wedgeDistance(float radius, float scale)
{
    float pixelScale = radiusPixels * scale;
    float radialInside = min(radius - innerRadius, outerRadius - radius) * pixelScale;
    float angle = atan(radialPosition.x, -radialPosition.y);
    float wedgeCount = float(entryCount - 1);
    float centerAngle = startAngleRadians + clockwiseSign * float(entryIndex) * 6.28318530718 / wedgeCount;
    float delta = atan(sin(angle - centerAngle), cos(angle - centerAngle));
    float halfAngle = 3.14159265359 / wedgeCount - separatorFraction * 6.28318530718 / wedgeCount;
    float angularInside = radius * sin(halfAngle - abs(delta)) * pixelScale;
    float edge = min(radialInside, angularInside);
    float corner = min(cornerRadiusPixels, max(0.0, min((outerRadius - innerRadius) * pixelScale * 0.5,
        radius * sin(halfAngle) * pixelScale * 0.5)));
    if (radialInside < corner && angularInside < corner)
        edge = min(edge, corner - length(vec2(corner - radialInside, corner - angularInside)));
    return edge;
}

/* Shades and masks the same rounded wedge contour. */
void main()
{
    if (entryIndex < 0 || entryIndex >= entryCount) discard;
    bool center = entryIndex == entryCount - 1;
    float radius = length(radialPosition);
    float scale = center ? 1.0 : mix(1.0, hoverScale, entryStates[entryIndex].z);
    float distancePixels;
    if (center) distancePixels = (centerRadius - radius) * radiusPixels;
    else distancePixels = wedgeDistance(radius, scale);
    float aa = max(fwidth(distancePixels), 0.75);
    float coverage = smoothstep(-aa, aa, distancePixels);
    if (maskIndex >= 0)
    {
        if (entryIndex != maskIndex || coverage < 0.5) discard;
        fragColor = vec4(1.0);
        return;
    }

    vec4 state = entryStates[entryIndex];
    float enabled = state.x;
    float hover = center ? float(entryIndex == hoveredIndex) : state.z;
    vec3 baseColor = mix(disabledFill, enabledFill, enabled);
    vec3 fill = mix(baseColor, hoverFill, hover * enabled);
    fill = mix(fill, selectedFill, state.y);
    if (!center && grainStrength > 0.0)
        fill += vec3(grain(radialPosition * radiusPixels / 6.0) * grainStrength);

    if (!center)
    {
        float border = 1.0 - smoothstep(borderWidthPixels - aa, borderWidthPixels + aa, distancePixels);
        vec3 bronze = mix(borderColor, hoverBorderColor, hover * enabled);
        fill = mix(fill, bronze, border);
    }
    fragColor = vec4(fill, coverage * mix(disabledOpacity, enabledOpacity, enabled));
}
