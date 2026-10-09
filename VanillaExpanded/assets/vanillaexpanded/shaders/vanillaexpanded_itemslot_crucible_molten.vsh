#version 330 core

layout(location = 0) in vec3 vertex;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform vec4 slotBounds;
uniform float fill;
uniform vec4 effectParameters;
uniform int segmentCount;
uniform samplerBuffer liquidSurface;
uniform int surfaceCellCount;
out float surfaceDepth;

/** Interpolates the shared cell-centered surface and adds edge wetting. */
float rawSurface(float u)
{
    float cell = clamp(u * float(surfaceCellCount) - 0.5, 0.0, float(surfaceCellCount - 1));
    int left = int(floor(cell));
    int right = min(left + 1, surfaceCellCount - 1);
    float displacement = mix(texelFetch(liquidSurface, left).r, texelFetch(liquidSurface, right).r, fract(cell));
    float inverseSegmentsSquared = 1.0 / (float(segmentCount) * float(segmentCount));
    float edgeMean = 0.2 + (4.0 / 3.0) * inverseSegmentsSquared
        - (8.0 / 15.0) * inverseSegmentsSquared * inverseSegmentsSquared;
    float centered = 2.0 * u - 1.0;
    float edgeShape = centered * centered;
    edgeShape *= edgeShape;
    float meniscus = effectParameters.w * (edgeShape - edgeMean) / (1.0 - edgeMean);
    return displacement * effectParameters.x * 8.0 + meniscus;
}

/** Maps the shared fluid surface into the slot while retaining sampled area and a uniform displacement bound. */
void main()
{
    float u = vertex.x;
    float mean = 0.0;
    float peak = 0.0;
    // Cell centers and mesh vertices sample different grids. Correct the actual mesh's trapezoidal mean,
    // then scale the entire profile uniformly rather than clipping peaks and changing represented fill.
    for (int sample = 0; sample <= segmentCount; sample++)
    {
        float value = rawSurface(float(sample) / float(segmentCount));
        mean += value * ((sample == 0 || sample == segmentCount) ? 0.5 : 1.0);
        peak = max(peak, abs(value));
    }
    mean /= float(segmentCount);
    float displacement = (rawSurface(u) - mean) / max(1.0, peak + abs(mean));
    float allowance = min(0.1, min(0.25 * fill, 0.25 * (1.0 - fill)));
    float height = fill + allowance * displacement;
    surfaceDepth = height * (1.0 - vertex.y);
    vec2 position = vec2(slotBounds.x + u * slotBounds.z,
        slotBounds.y + slotBounds.w * (1.0 - vertex.y * height));
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 80.0, 1.0);
}
