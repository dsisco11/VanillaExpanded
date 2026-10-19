#version 330 core
in vec2 habitatUv;
out vec4 fragColor;
uniform isampler2D climateMap;
uniform isampler2D forestMap;
uniform isampler2D shrubMap;
uniform usampler2D terrainMap;
uniform usampler2D exploredMap;
uniform vec3 climateLayout;
uniform vec3 forestLayout;
uniform vec3 shrubLayout;
uniform vec2 tileOrigin;
uniform vec2 worldSize;
uniform float chunkSize;
uniform float regionSize;
uniform float seaLevel;
uniform float worldHeight;
uniform float temperatureScale;
uniform int worldgenTemperatureDistance;
uniform int runtimeReferenceHeight;
uniform int terrainAvailable;
uniform int samplingPath;
uniform vec2 temperatureRange;
uniform vec2 rainfallRange;
uniform vec2 forestRange;
uniform vec2 shrubRange;
uniform vec2 elevationRange;
uniform float minForestOrShrubs;
uniform vec4 suitableColor;
uniform vec4 unavailableColor;

// Compare a single active range set. Missing inputs never become zero-valued habitat samples.
/** Interpolates four original samples in vanilla top/bottom order. */
float bilerp(vec4 a, vec2 f) {
    return (a.x + (a.y-a.x)*f.x) + ((a.z+(a.w-a.z)*f.x)-(a.x+(a.y-a.x)*f.x))*f.y;
}
/** Samples one original map with its own padding and the active spawn sampling convention. */
float readMap(isampler2D source, vec3 mapLayout, ivec2 column, bool color, out bool available) {
    available = false;
    if (mapLayout.x <= 0.0) return 0.0;
    float inner = mapLayout.x - mapLayout.y - mapLayout.z;
    vec2 pos, end, f;
    if (samplingPath == 0) {
        float stepSize = inner / (regionSize / chunkSize);
        pos = mod(tileOrigin / chunkSize, regionSize / chunkSize) * stepSize;
        end = trunc(pos + stepSize);
        pos = trunc(pos);
        f = (vec2(column) + 0.5) / chunkSize;
    } else {
        pos = mod(tileOrigin + vec2(column), regionSize) / regionSize * inner;
        f = fract(pos);
        pos = trunc(pos);
        end = pos + 1.0;
    }
    ivec2 a = ivec2(pos + mapLayout.y), b = ivec2(end + mapLayout.y);
    if (any(lessThan(a, ivec2(0))) || any(greaterThanEqual(b, ivec2(mapLayout.x)))) return 0.0;
    ivec4 values = ivec4(texelFetch(source,a,0).r, texelFetch(source,ivec2(b.x,a.y),0).r,
        texelFetch(source,ivec2(a.x,b.y),0).r, texelFetch(source,b,0).r);
    available = true;
    if (!color) return bilerp(vec4(values), f);
    // Preserve vanilla's per-channel mask after interpolation, including its truncation order.
    int packedValue = 0;
    for (int shift=0; shift<=16; shift+=8) {
        int mask = 255 << shift;
        packedValue += int(bilerp(vec4(values & ivec4(mask)), f)) & mask;
    }
    return float(packedValue);
}
/** Checks inclusive habitat bounds. */
bool inside(float value, vec2 range) { return value >= range.x && value <= range.y; }
/** Evaluates one active range set and emits highlight or unavailable crosshatching on explored columns. */
void main() {
    ivec2 column = clamp(ivec2(floor(habitatUv * chunkSize)), ivec2(0), ivec2(chunkSize-1.0));
    if (texelFetch(exploredMap,column,0).r == 0u) discard;
    vec2 worldPos = tileOrigin + vec2(column);
    if (any(lessThan(worldPos,vec2(0))) || any(greaterThanEqual(worldPos,worldSize))) discard;
    bool climateOk, forestOk, shrubOk;
    int climate = int(readMap(climateMap,climateLayout,column,true,climateOk));
    float forest = readMap(forestMap,forestLayout,column,samplingPath!=0,forestOk) / 255.0;
    float shrubs = readMap(shrubMap,shrubLayout,column,samplingPath!=0,shrubOk);
    shrubs = (samplingPath==0 ? shrubs : float(int(shrubs)&255)) / 255.0;
    float distance = samplingPath==0 ? float(worldgenTemperatureDistance) : float(runtimeReferenceHeight)-seaLevel;
    float temperature = clamp((float((climate>>16)&255)-distance/1.5)/temperatureScale-20.0,-20.0,40.0);
    int rain = (climate>>8)&255;
    if (samplingPath!=0) {
        int referenceY = runtimeReferenceHeight;
        rain = clamp(rain + (referenceY-int(seaLevel))/2 + 5*clamp(8+int(seaLevel)-referenceY,0,8),0,255);
    }
    float rainfall = float(rain)/255.0;
    float y = float(texelFetch(terrainMap,column,0).r)+1.0;
    if (terrainAvailable!=0 && y>=worldHeight) discard;
    float elevation = y<=seaLevel ? y/seaLevel : 1.0+(y-seaLevel)/(worldHeight-seaLevel);
    // A known mismatch is transparent even if another required input is unavailable.
    if ((climateOk && (!inside(temperature,temperatureRange)||!inside(rainfall,rainfallRange))) ||
        (forestOk && !inside(forest,forestRange)) || (shrubOk && !inside(shrubs,shrubRange)) ||
        (forestOk && shrubOk && max(forest,shrubs)<minForestOrShrubs) ||
        (terrainAvailable!=0 && !inside(elevation,elevationRange))) discard;
    if (!climateOk || !forestOk || !shrubOk || terrainAvailable==0) {
        vec2 pixel = gl_FragCoord.xy;
        bool hatch = mod(pixel.x+pixel.y,8.0)<1.5 || mod(pixel.x-pixel.y,8.0)<1.5;
        if (!hatch) discard;
        fragColor = unavailableColor;
    } else fragColor = suitableColor;
}
