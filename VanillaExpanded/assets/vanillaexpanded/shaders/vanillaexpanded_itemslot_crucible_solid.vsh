#version 330 core
layout(location = 0) in vec3 position;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform vec4 slotBounds;
uniform float fill;
uniform samplerBuffer grainState;
uniform vec4 metalPalette[16];
out vec2 fragmentLocal;
out vec2 slotLocal;
flat out float shade;
flat out vec3 particleColor;
flat out vec3 cellPlanes[6];

/** Produces stable shape samples independently of rotation and palette selection. */
float shapeRandom(uint seed, uint index)
{
    uint hash = seed ^ (index * 0x9e3779b9u);
    hash = (hash ^ (hash >> 16u)) * 0x85ebca6bu;
    hash = (hash ^ (hash >> 13u)) * 0xc2b2ae35u;
    return float((hash ^ (hash >> 16u)) >> 8u) / 16777216.0;
}

/** Expands 128 angular metal pieces from independent high-friction granular state. */
void main()
{
    int i = int(position.z + 0.5);
    // Metal pieces are much coarser than food: one in three quads is visible.
    if (i % 3 != 0)
    {
        fragmentLocal = vec2(2.0);
        slotLocal = vec2(2.0);
        shade = 1.0;
        particleColor = vec3(0.0);
        for (int face = 0; face < 6; face++) cellPlanes[face] = vec3(1.0, 0.0, 2.0);
        gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
        return;
    }
    // The cached 384-quad geometry emits one visible quad per three indices; state is compact at 128 chunks.
    int particle = i / 3;
    vec4 pv = texelFetch(grainState, 2 * particle);
    vec4 pr = texelFetch(grainState, 2 * particle + 1);
    uint hash = uint(i) + 0x9e3779b9u;
    hash = (hash ^ (hash >> 16u)) * 0x85ebca6bu;
    hash = (hash ^ (hash >> 13u)) * 0xc2b2ae35u;
    hash ^= hash >> 16u;
    // Prepare random Voronoi bisectors per vertex; fragments only perform cheap half-plane tests.
    for (int face = 0; face < 6; face++)
    {
        float normalAngle = float(face) * 1.04719755 + mix(-0.22, 0.22, shapeRandom(hash, uint(face * 2 + 1)));
        float distance = mix(0.6, 0.94, shapeRandom(hash, uint(face * 2 + 2)));
        cellPlanes[face] = vec3(cos(normalAngle), sin(normalAngle), distance);
    }
    float angle = float(hash >> 8u) / 16777216.0 * 6.2831853;
    mat2 rotation = mat2(cos(angle), sin(angle), -sin(angle), cos(angle));
    // Larger collision radii need less visual enlargement; keep pieces within the minimum 0.15 fill envelope.
    float radius = pr.z * 1.8;
    vec2 center = vec2(clamp(pv.x, radius, 1.0 - radius),
        radius + (pv.y - pr.z) * max(0.0, fill - 2.0 * radius) / max(pr.w - 2.0 * pr.z, 0.001));
    fragmentLocal = position.xy;
    slotLocal = center + rotation * (position.xy * radius * vec2(1.0, 0.8));
    shade = mix(0.85, 1.15, float(hash & 255u) / 255.0);
    particleColor = metalPalette[int((hash >> 4u) & 15u)].rgb;
    vec2 pixel = vec2(slotBounds.x + slotLocal.x * slotBounds.z,
        slotBounds.y + (1.0 - slotLocal.y) * slotBounds.w);
    gl_Position = projectionMatrix * modelViewMatrix * vec4(pixel, 80.0, 1.0);
}
