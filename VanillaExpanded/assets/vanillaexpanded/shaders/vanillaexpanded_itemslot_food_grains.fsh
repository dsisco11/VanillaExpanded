#version 330 core
uniform vec4 color;
uniform vec4 effectParameters;
in vec2 grainLocal;
in vec2 slotLocal;
flat in vec3 grainColor;
flat in vec3 cellPlanes[6];
layout(location=0) out vec4 fragColor;

/** Clips four or five active Voronoi bisectors against the quad; remaining planes are inactive. */
float cellBoundary(vec2 point)
{
    float boundary = max(abs(point.x), abs(point.y)) - 0.98;
    for (int face = 0; face < 6; face++)
        boundary = max(boundary, dot(point, cellPlanes[face].xy) - cellPlanes[face].z);
    return boundary;
}

/** Draws angular food pieces with nearly opaque interiors and antialiased edges. */
void main()
{
    if (any(lessThan(slotLocal,vec2(0.0))) || any(greaterThan(slotLocal,vec2(1.0)))) discard;
    float boundary = cellBoundary(grainLocal);
    float edge = max(fwidth(boundary),0.0001);
    // A one-pixel linear coverage ramp preserves short straight edges at inventory-slot scale.
    float coverage = clamp(0.5-boundary/edge,0.0,1.0);
    if (coverage <= 0.0) discard;
    float alpha = clamp(color.a*effectParameters.x,0.0,1.0)*coverage;
    fragColor = vec4(grainColor*alpha,alpha);
}
