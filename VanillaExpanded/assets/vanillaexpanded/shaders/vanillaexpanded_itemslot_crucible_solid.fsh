#version 330 core
uniform vec4 color;
uniform vec4 effectParameters;
in vec2 fragmentLocal;
in vec2 slotLocal;
flat in float shade;
flat in vec3 particleColor;
flat in vec3 cellPlanes[6];
layout(location = 0) out vec4 fragColor;

/** Intersects the origin site's six Voronoi bisectors with a slightly inset quad envelope. */
float cellBoundary(vec2 point)
{
    float boundary = max(abs(point.x), abs(point.y)) - 0.98;
    for (int face = 0; face < 6; face++)
    {
        // Each neighbor site lies at normal * 2 * distance. Its bisector clips a random angular cell.
        boundary = max(boundary, dot(point, cellPlanes[face].xy) - cellPlanes[face].z);
    }
    return boundary;
}

/** Draws distinct cellular metal silhouettes with nearly opaque interiors and antialiased edges. */
void main()
{
    if (any(lessThan(slotLocal, vec2(0.0))) || any(greaterThan(slotLocal, vec2(1.0)))) discard;
    float boundary = cellBoundary(fragmentLocal);
    float aa = max(fwidth(boundary), 0.0001);
    float coverage = 1.0 - smoothstep(-aa, aa, boundary);
    if (coverage <= 0.0) discard;
    float facet = mix(0.85, 1.2, step(fragmentLocal.x + fragmentLocal.y, 0.0));
    float alpha = clamp(color.a * effectParameters.x, 0.0, 1.0) * coverage;
    fragColor = vec4(clamp(particleColor * shade * facet, 0.0, 1.0) * alpha, alpha);
}
