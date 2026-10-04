#version 330 core

layout(location = 0) in vec3 vertex;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform vec4 slotBounds;
uniform float fill;
uniform float timeSeconds;
uniform vec2 motion;
uniform vec4 effectParameters;
uniform int segmentCount;

/* Oscillates the center against both sides and raises the edges without changing sampled fill area. */
void main()
{
    float u = vertex.x;
    // Integral cycles over the shared clock period avoid a jump when time wraps.
    float phase = 6.28318530718 * timeSeconds / 4.0;
    float activity = max(abs(motion.x), abs(motion.y));
    float wave = -(effectParameters.x + effectParameters.y * activity)
        * cos(6.28318530718 * u) * cos(phase);
    // A quartic edge rise suggests a meniscus. Subtract its exact strip-sampled mean,
    // then normalize its largest excursion so the parameter remains a displacement weight.
    float inverseSegmentsSquared = 1.0 / (float(segmentCount) * float(segmentCount));
    float edgeMean = 0.2 + (4.0 / 3.0) * inverseSegmentsSquared
        - (8.0 / 15.0) * inverseSegmentsSquared * inverseSegmentsSquared;
    float centered = 2.0 * u - 1.0;
    float edgeShape = centered * centered;
    edgeShape *= edgeShape;
    float meniscus = effectParameters.w * (edgeShape - edgeMean) / (1.0 - edgeMean);
    // Looking right raises the left edge, opposing the camera turn.
    float tilt = -effectParameters.z * motion.x * (2.0 * u - 1.0);
    // The weights total at most one. Bound the whole deformation instead of clipping individual heights.
    float allowance = min(0.1, min(0.25 * fill, 0.25 * (1.0 - fill)));
    float height = fill + allowance * (wave + tilt + meniscus);
    vec2 position = vec2(slotBounds.x + u * slotBounds.z,
        slotBounds.y + slotBounds.w * (1.0 - vertex.y * height));
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 80.0, 1.0);
}
