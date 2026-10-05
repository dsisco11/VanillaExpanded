#version 330 core
layout(location=0) in vec3 position;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform vec4 slotBounds;
uniform float fill;
uniform float resourceFill;
uniform samplerBuffer grainState;
out vec2 grainLocal;
out vec2 slotLocal;
uniform vec4 foodPalette[16];
flat out vec3 grainColor;
flat out vec3 cellPlanes[6];

/** Produces stable Voronoi neighbor samples independently of orientation and food color. */
float shapeRandom(uint seed, uint index)
{
    uint hash = seed ^ (index * 0x9e3779b9u);
    hash = (hash ^ (hash >> 16u)) * 0x85ebca6bu;
    hash = (hash ^ (hash >> 13u)) * 0xc2b2ae35u;
    return float((hash ^ (hash >> 16u)) >> 8u) / 16777216.0;
}

/** Expands one cached quad per food piece from shared state, preserving its screen-space size. */
void main()
{
    int i = int(position.z+0.5);
    // A stable prefix of randomly placed particles represents quantity without shrinking grains.
    // Cull all four corners together before state reads; empty contents emit no visible geometry.
    if (i >= int(ceil(clamp(resourceFill, 0.0, 1.0)*384.0)))
    {
        grainLocal = vec2(2.0);
        slotLocal = vec2(2.0);
        grainColor = vec3(0.0);
        for (int face = 0; face < 6; face++) cellPlanes[face] = vec3(1.0, 0.0, 2.0);
        gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
        return;
    }
    vec4 pv = texelFetch(grainState, 2*i);
    vec4 pr = texelFetch(grainState, 2*i+1);
    // Avalanche the identity so orientation has no repeating sequence across neighboring grains.
    uint hash = uint(i) + 0x9e3779b9u;
    hash = (hash ^ (hash >> 16u)) * 0x85ebca6bu;
    hash = (hash ^ (hash >> 13u)) * 0xc2b2ae35u;
    hash ^= hash >> 16u;
    // Four or five neighbors leave longer edges and stronger corners than a near-round hexagon.
    int faces = 4 + int((hash >> 2u) & 1u);
    for (int face = 0; face < 6; face++)
    {
        float normalAngle = float(face) * 6.2831853 / float(faces) + mix(-0.18, 0.18, shapeRandom(hash, uint(face * 2 + 1)));
        float distance = mix(0.45, 0.94, shapeRandom(hash, uint(face * 2 + 2)));
        cellPlanes[face] = face < faces ? vec3(cos(normalAngle), sin(normalAngle), distance) : vec3(1.0, 0.0, 2.0);
    }
    float seed = float(hash >> 8u)/16777216.0;
    float angle = seed*6.2831853;
    mat2 rotation = mat2(cos(angle), sin(angle), -sin(angle), cos(angle));
    grainLocal = position.xy;
    // Tiny two-pixel footprints cannot show cell corners; retain a minimum visible food-piece size.
    float visualRadius = max(0.05, pr.z*3.0);
    float aspect = mix(0.55, 0.9, shapeRandom(hash, 17u));
    vec2 offset = rotation*(position.xy*visualRadius*vec2(1.0,aspect));
    // Contain the enlarged rotated quad, including corners outside its circular collision radius.
    vec2 extent = visualRadius*vec2(abs(cos(angle))+aspect*abs(sin(angle)), abs(sin(angle))+aspect*abs(cos(angle)));
    // Amount maps the remaining centers into the draw range, never individual grain dimensions.
    vec2 center = vec2(clamp(pv.x,extent.x,1.0-extent.x),
        extent.y+(pv.y-pr.z)*max(0.0,fill-2.0*extent.y)/max(pr.w-2.0*pr.z,0.001));
    // Keep individual excursions inside the food effect's upper draw level without rescaling the pile.
    center.y = min(center.y, 0.85-extent.y);
    slotLocal = center+offset;
    // Keep color selection independent of rotation; identity makes it stable as the grain moves.
    grainColor = foodPalette[int((hash >> 4u) & 15u)].rgb;
    vec2 pixel = vec2(slotBounds.x+slotLocal.x*slotBounds.z, slotBounds.y+(1.0-slotLocal.y)*slotBounds.w);
    gl_Position = projectionMatrix*modelViewMatrix*vec4(pixel,80.0,1.0);
}
