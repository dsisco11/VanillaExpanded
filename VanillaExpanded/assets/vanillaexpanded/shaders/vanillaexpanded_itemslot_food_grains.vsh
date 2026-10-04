#version 330 core
layout(location=0) in vec3 position;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform vec4 slotBounds;
uniform float fill;
uniform samplerBuffer grainState;
out vec2 grainLocal;
out vec2 slotLocal;
flat out float shade;

/* Expands one cached quad per grain from shared state, preserving its screen-space size. */
void main()
{
    int i = int(position.z+0.5);
    vec4 pv = texelFetch(grainState, 2*i);
    vec4 pr = texelFetch(grainState, 2*i+1);
    // Avalanche the identity so orientation has no repeating sequence across neighboring grains.
    uint hash = uint(i) + 0x9e3779b9u;
    hash = (hash ^ (hash >> 16u)) * 0x85ebca6bu;
    hash = (hash ^ (hash >> 13u)) * 0xc2b2ae35u;
    hash ^= hash >> 16u;
    float seed = float(hash >> 8u)/16777216.0;
    float angle = seed*6.2831853;
    mat2 rotation = mat2(cos(angle), sin(angle), -sin(angle), cos(angle));
    grainLocal = position.xy;
    float visualRadius = pr.z*1.6;
    // A more elongated silhouette makes random rotation legible at inventory-slot sizes.
    vec2 offset = rotation*(position.xy*visualRadius*vec2(1.0,0.58));
    // Freshness maps the pile's center positions, never its physical simulation or individual grain size.
    vec2 center = vec2(clamp(pv.x,visualRadius,1.0-visualRadius),
        visualRadius+(pv.y-pr.z)*max(0.0,fill-2.0*visualRadius)/max(pr.w-2.0*pr.z,0.001));
    slotLocal = center+offset;
    shade = 0.78+0.22*seed;
    vec2 pixel = vec2(slotBounds.x+slotLocal.x*slotBounds.z, slotBounds.y+(1.0-slotLocal.y)*slotBounds.w);
    gl_Position = projectionMatrix*modelViewMatrix*vec4(pixel,80.0,1.0);
}
