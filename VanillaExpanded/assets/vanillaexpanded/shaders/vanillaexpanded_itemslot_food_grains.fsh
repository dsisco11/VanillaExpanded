#version 330 core
uniform vec4 color;
uniform vec4 effectParameters;
uniform float fill;
in vec2 grainLocal;
in vec2 slotLocal;
flat in vec3 grainColor;
flat in vec3 cellPlanes[6];
flat in mat2 surfaceBasis;
layout(location=0) out vec4 fragColor;

/** Clips active Voronoi planes and supplies the raised-center roof coordinate and local surface slope. */
float cellBoundary(vec2 point, out vec2 slope, out float roof)
{
    float boundary = max(abs(point.x), abs(point.y)) - 0.98;
    // The largest normalized plane coordinate selects a facet of a raised-center roof.
    roof = max(abs(point.x), abs(point.y))/0.98;
    slope = abs(point.x) > abs(point.y) ? vec2(sign(point.x)/0.98,0.0) : vec2(0.0,sign(point.y)/0.98);
    for (int face = 0; face < 6; face++)
    {
        vec3 plane = cellPlanes[face];
        boundary = max(boundary, dot(point, plane.xy) - plane.z);
        float coordinate = dot(point,plane.xy)/plane.z;
        if (coordinate > roof) { roof = coordinate; slope = plane.xy/plane.z; }
    }
    return boundary;
}

/** Draws angular food pieces with nearly opaque interiors and antialiased edges. */
void main()
{
    if (any(lessThan(slotLocal,vec2(0.0))) || any(greaterThan(slotLocal,vec2(1.0)))) discard;
    vec2 slope;
    float roof;
    float boundary = cellBoundary(grainLocal, slope, roof);
    float edge = max(fwidth(boundary),0.0001);
    // A one-pixel linear coverage ramp preserves short straight edges at inventory-slot scale.
    float coverage = clamp(0.5-boundary/edge,0.0,1.0);
    if (coverage <= 0.0) discard;
    float alpha = clamp(color.a*effectParameters.x,0.0,1.0)*coverage;
    // Shade against the displayed pile height so low contents retain the same top-to-bottom lighting.
    // Preserve ingredient hues and opacity; only RGB illumination changes with depth in the pile.
    float relativeHeight = clamp(slotLocal.y/max(fill,0.001),0.0,1.0);
    float light = mix(0.60,1.0,relativeHeight);
    // Soft matte facets rise toward the center; normals rotate with the cell, not with the light.
    vec3 normal = normalize(vec3(surfaceBasis*slope*0.35*smoothstep(0.0,0.35,roof),1.0));
    vec3 direction = normalize(vec3(0.45,0.65,0.62));
    float surfaceLight = 0.55+0.45*max(dot(normal,direction),0.0);
    fragColor = vec4(grainColor*surfaceLight*light*alpha,alpha);
}
