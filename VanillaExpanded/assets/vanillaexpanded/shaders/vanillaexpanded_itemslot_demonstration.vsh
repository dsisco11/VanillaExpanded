#version 330 core

layout(location = 0) in vec3 vertex;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform vec4 slotBounds;
uniform float fill;
uniform float timeSeconds;
uniform vec2 motion;
uniform vec4 effectParameters;

/* Deforms only the surface within the shared fill/area contract, using fixed reusable geometry. */
void main()
{
    float u = vertex.x;
    // Both sin(2*pi*u) and 2*u-1 have zero trapezoidal mean at every supported equal-width subdivision.
    // The coefficients sum to at most one in absolute magnitude, so the shared height bound is preserved.
    float wave = sin(6.28318530718 * u) * cos(6.28318530718 * timeSeconds / 4.0);
    float shape = 0.5 * wave * (0.75 + 0.25 * motion.y) + 0.5 * (2.0 * u - 1.0) * motion.x;
    float amplitude = min(0.1, min(0.25 * fill, 0.25 * (1.0 - fill))) * effectParameters.x;
    float height = fill + amplitude * shape;
    vec2 position = vec2(slotBounds.x + u * slotBounds.z,
        slotBounds.y + slotBounds.w * (1.0 - vertex.y * height));
    // Edge zero stays bottom-anchored; empty/full fills have zero deformation. Time is periodic over 64 seconds.
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 80.0, 1.0);
}
