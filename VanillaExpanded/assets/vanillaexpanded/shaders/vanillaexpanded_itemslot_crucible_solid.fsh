#version 330 core
uniform vec4 color;
uniform vec4 effectParameters;
uniform float fill;
in vec2 fragmentLocal;
in vec2 slotLocal;
flat in float shade;
flat in vec3 particleColor;
flat in vec3 cellPlanes[6];
flat in mat2 surfaceBasis;
layout(location = 0) out vec4 fragColor;

/** Intersects the cell planes with the quad envelope and selects the raised-center roof's local slope. */
float cellBoundary(vec2 point, out vec2 slope)
{
    float boundary = max(abs(point.x), abs(point.y)) - 0.98;
    float roof = max(abs(point.x),abs(point.y))/0.98;
    slope = abs(point.x) > abs(point.y) ? vec2(sign(point.x)/0.98,0.0) : vec2(0.0,sign(point.y)/0.98);
    for (int face = 0; face < 6; face++)
    {
        // Each neighbor site lies at normal * 2 * distance. Its bisector clips a random angular cell.
        boundary = max(boundary, dot(point, cellPlanes[face].xy) - cellPlanes[face].z);
        float coordinate = dot(point,cellPlanes[face].xy)/cellPlanes[face].z;
        if (coordinate > roof) { roof = coordinate; slope = cellPlanes[face].xy/cellPlanes[face].z; }
    }
    return boundary;
}

/** Draws distinct cellular metal silhouettes with nearly opaque interiors and antialiased edges. */
void main()
{
    if (any(lessThan(slotLocal, vec2(0.0))) || any(greaterThan(slotLocal, vec2(1.0)))) discard;
    vec2 slope;
    float boundary = cellBoundary(fragmentLocal,slope);
    float aa = max(fwidth(boundary), 0.0001);
    float coverage = 1.0 - smoothstep(-aa, aa, boundary);
    if (coverage <= 0.0) discard;
    float alpha = clamp(color.a * effectParameters.x, 0.0, 1.0) * coverage;
    // Normalize lighting by the displayed pile height, preserving the gradient at different quantities.
    float relativeHeight = clamp(slotLocal.y / max(fill, 0.001), 0.0, 1.0);
    float light = mix(0.70, 1.0, relativeHeight);
    // Actual cell planes define planar slopes toward the raised center, lit from screen upper-right.
    vec3 normal = normalize(vec3(surfaceBasis*slope*0.55,1.0));
    vec3 direction = normalize(vec3(0.45,0.65,0.62));
    float surfaceLight = 0.40+0.60*max(dot(normal,direction),0.0);
    vec3 halfDirection = normalize(direction+vec3(0.0,0.0,1.0));
    float highlight = 0.08*pow(max(dot(normal,halfDirection),0.0),24.0);
    fragColor = vec4(clamp(particleColor*shade*surfaceLight+vec3(highlight),0.0,1.0)*light*alpha,alpha);
}
