#version 330 core
in vec2 habitatUv;
out vec4 fragColor;
uniform sampler2D regionalMap;
uniform usampler2D terrainMap;
uniform vec2 regionalAtlasOrigin;
uniform float regionalAtlasSize;
uniform vec2 terrainAtlasOrigin;
uniform vec4 climateLayout;
uniform vec4 forestLayout;
uniform vec4 shrubLayout;
uniform vec2 tileOrigin;
uniform vec2 worldSize;
uniform float chunkSize;
uniform float regionSize;
uniform float seaLevel;
uniform float worldHeight;
uniform float temperatureScale;
uniform int worldgenTemperatureDistance;
uniform int runtimeReferenceHeight;
uniform int samplingPath;
uniform vec2 temperatureRange;
uniform vec2 rainfallRange;
uniform vec2 forestRange;
uniform vec2 shrubRange;
uniform vec2 surfaceHeightRange;
uniform float minForestOrShrubs;
uniform vec4 suitableColor;
uniform vec4 unavailableColor;

/** Native logical corner selection plus its aligned physical texture positions. */
struct MapCoordinates {
    ivec2 a;
    ivec2 b;
    vec2 fraction;
    bool available;
};
/** Keeps each grid's native world mapping and padding, independently of its upload offset. */
MapCoordinates mapCoordinates(vec4 mapLayout, ivec2 column) {
    MapCoordinates result = MapCoordinates(ivec2(0),ivec2(0),vec2(0.0),false);
    if (mapLayout.x <= 0.0) return result;
    float inner = mapLayout.x - mapLayout.y - mapLayout.z;
    vec2 pos, end;
    if (samplingPath == 0) {
        float stepSize = inner / (regionSize / chunkSize);
        pos = mod(tileOrigin / chunkSize, regionSize / chunkSize) * stepSize;
        end = trunc(pos + stepSize);
        pos = trunc(pos);
        result.fraction = (vec2(column) + 0.5) / chunkSize;
    } else {
        pos = mod(tileOrigin + vec2(column), regionSize) / regionSize * inner;
        result.fraction = fract(pos);
        pos = trunc(pos);
        end = pos + 1.0;
    }
    ivec2 a = ivec2(pos + mapLayout.y), b = ivec2(end + mapLayout.y);
    if (any(lessThan(a,ivec2(0))) || any(greaterThanEqual(b,ivec2(mapLayout.x)))) return result;
    result.a = a + ivec2(mapLayout.w) + ivec2(regionalAtlasOrigin);
    result.b = b + ivec2(mapLayout.w) + ivec2(regionalAtlasOrigin);
    result.available = true;
    return result;
}
/** Reuses all RGBA channels when logical grids select the same physical neighborhood and weights. */
bool sameCoordinates(MapCoordinates a, MapCoordinates b) {
    return a.available && b.available && all(equal(a.a,b.a)) && all(equal(a.b,b.b)) && all(equal(a.fraction,b.fraction));
}
/** Filters interior adjacent corners; exact edge reads isolate slots despite normalized-coordinate rounding. */
vec4 sampleMap(MapCoordinates coordinates) {
    if (!coordinates.available) return vec4(0.0);
    ivec2 a = coordinates.a, b = coordinates.b;
    vec2 f = coordinates.fraction;
    // An interior one-texel margin absorbs filtering-coordinate precision without touching another allocation.
    ivec2 first = ivec2(regionalAtlasOrigin), last = first + ivec2(regionalAtlasSize-1.0);
    bool interior = all(greaterThan(a,first)) && all(lessThan(b,last));
    if (interior && all(lessThanEqual(b-a,ivec2(1)))) {
        vec2 position = mix(vec2(a),vec2(b),f) + 0.5;
        return textureLod(regionalMap,position/vec2(textureSize(regionalMap,0)),0.0);
    }
    // Allocation edges cannot use page-level clamping. Non-adjacent worldgen corners also require exact reads.
    vec4 top = texelFetch(regionalMap,a,0);
    vec4 right = texelFetch(regionalMap,ivec2(b.x,a.y),0);
    vec4 bottom = texelFetch(regionalMap,ivec2(a.x,b.y),0);
    vec4 corner = texelFetch(regionalMap,b,0);
    vec4 upper = top + (right-top)*f.x;
    vec4 lower = bottom + (corner-bottom)*f.x;
    return upper + (lower-upper)*f.y;
}
/** Restores whole-byte spawn sampling after normalized filtering without rounding up fractional values. */
float sampledByte(float value) { return floor(clamp(value,0.0,1.0)*255.0); }
/** Checks inclusive habitat bounds. */
bool inside(float value, vec2 range) { return value >= range.x && value <= range.y; }
/** Evaluates one active range set and emits highlight or unavailable crosshatching on explored columns. */
void main() {
    ivec2 column = clamp(ivec2(floor(habitatUv * chunkSize)), ivec2(0), ivec2(chunkSize-1.0));
    uvec2 terrain = texelFetch(terrainMap,column+ivec2(terrainAtlasOrigin),0).rg;
    uint flags = terrain.g;
    bool terrainAvailable = (flags & 1u) != 0u;
    // Current explored-chunk membership gates every draw; no redundant exploration texture is stored.
    // Ocean suppression precedes all habitat and unavailable-data output, even without height or climate.
    if ((flags & 4u) != 0u) discard;
    vec2 worldPos = tileOrigin + vec2(column);
    if (any(lessThan(worldPos,vec2(0))) || any(greaterThanEqual(worldPos,worldSize))) discard;
    MapCoordinates climateCoordinates = mapCoordinates(climateLayout,column);
    MapCoordinates forestCoordinates = mapCoordinates(forestLayout,column);
    MapCoordinates shrubCoordinates = mapCoordinates(shrubLayout,column);
    bool climateOk = climateCoordinates.available, forestOk = forestCoordinates.available, shrubOk = shrubCoordinates.available;
    vec4 climateSample = sampleMap(climateCoordinates);
    vec4 forestSample = sameCoordinates(climateCoordinates,forestCoordinates) ? climateSample : sampleMap(forestCoordinates);
    vec4 shrubSample;
    if (sameCoordinates(climateCoordinates,shrubCoordinates)) shrubSample = climateSample;
    else if (sameCoordinates(forestCoordinates,shrubCoordinates)) shrubSample = forestSample;
    else shrubSample = sampleMap(shrubCoordinates);
    float forest = samplingPath==0 ? forestSample.b : sampledByte(forestSample.b)/255.0;
    float shrubs = samplingPath==0 ? shrubSample.a : sampledByte(shrubSample.a)/255.0;
    float distance = samplingPath==0 ? float(worldgenTemperatureDistance) : float(runtimeReferenceHeight)-seaLevel;
    float temperature = clamp((sampledByte(climateSample.r)-distance/1.5)/temperatureScale-20.0,-20.0,40.0);
    int rain = int(sampledByte(climateSample.g));
    if (samplingPath!=0) {
        int referenceY = runtimeReferenceHeight;
        rain = clamp(rain + (referenceY-int(seaLevel))/2 + 5*clamp(8+int(seaLevel)-referenceY,0,8),0,255);
    }
    float rainfall = float(rain)/255.0;
    float y = float(terrain.r)+1.0;
    if (terrainAvailable && y>=worldHeight) discard;
    // Surface heights and supplied inclusive limits are exact integers in this texture range.
    // A known mismatch is transparent even if another required input is unavailable.
    if ((climateOk && (!inside(temperature,temperatureRange)||!inside(rainfall,rainfallRange))) ||
        (forestOk && !inside(forest,forestRange)) || (shrubOk && !inside(shrubs,shrubRange)) ||
        (forestOk && shrubOk && max(forest,shrubs)<minForestOrShrubs) ||
        (terrainAvailable && !inside(y,surfaceHeightRange))) discard;
    if ((flags & 2u)==0u || !climateOk || !forestOk || !shrubOk || !terrainAvailable) {
        vec2 pixel = gl_FragCoord.xy;
        bool hatch = mod(pixel.x+pixel.y,8.0)<1.5 || mod(pixel.x-pixel.y,8.0)<1.5;
        if (!hatch) discard;
        fragColor = unavailableColor;
    } else fragColor = suitableColor;
}
