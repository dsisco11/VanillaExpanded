#version 330 core

uniform samplerBuffer state;
uniform float timeStep;
uniform vec2 cameraAcceleration;
uniform float gravity;
uniform float damping;
uniform int cellCount;
uniform float cellSpacing;

out float nextHeight;
out float nextFlow;

/** Computes the next flux through a cell's right face from the immutable previous grid. */
float updatedFlow(int cell, float effectiveGravity, float decay)
{
    // Both walls are closed. The last stored flow is always the right-wall zero sentinel.
    if (cell < 0 || cell >= cellCount - 1) return 0.0;
    vec2 left = texelFetch(state, cell).rg;
    float rightHeight = texelFetch(state, cell + 1).r;
    float pressure = -effectiveGravity * (rightHeight - left.x) / cellSpacing;
    float facePosition = float(cell + 1) / float(cellCount);
    // Uniform lateral inertia moves liquid opposite acceleration. Vertical shaking also
    // seeds a zero-net-volume center/side disturbance; varying gravity alone cannot disturb a flat surface.
    float shaking = -0.2 * cameraAcceleration.y * sin(6.28318530718 * facePosition);
    float acceleration = pressure - cameraAcceleration.x + shaking;
    return (left.y + timeStep * acceleration) * decay;
}

/** Advances a conservative staggered linear shallow-water grid, one point per cell. */
void main()
{
    int cell = gl_VertexID;
    // The reference depth is one; height stores signed displacement about that reference.
    float effectiveGravity = clamp(gravity + cameraAcceleration.y, 0.1 * gravity, 4.0 * gravity);
    float decay = exp(-damping * timeStep);
    float incoming = updatedFlow(cell - 1, effectiveGravity, decay);
    nextFlow = updatedFlow(cell, effectiveGravity, decay);
    // Every interior face is computed identically by its neighboring cells, so fluxes
    // telescope to zero total height change without clipping or a global reduction.
    nextHeight = texelFetch(state, cell).r + timeStep * (incoming - nextFlow) / cellSpacing;
    gl_Position = vec4(0.0, 0.0, 0.0, 1.0);
}
