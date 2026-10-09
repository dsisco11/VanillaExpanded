#version 330 core

uniform samplerBuffer state;
uniform float timeStep;
uniform vec2 containerAcceleration;
uniform float verticalShapeVariation;
uniform float gravity;
uniform float damping;
uniform float wallDamping;
uniform float wallDampingWidth;
uniform int cellCount;
uniform float cellSpacing;

out float nextHeight;
out float nextFlow;

/** Computes the next flux through a cell's right face from the immutable previous grid. */
float updatedFlow(int cell, float effectiveGravity)
{
    // Both walls are closed. The last stored flow is always the right-wall zero sentinel.
    if (cell < 0 || cell >= cellCount - 1) return 0.0;
    vec2 left = texelFetch(state, cell).rg;
    float rightHeight = texelFetch(state, cell + 1).r;
    float pressure = -effectiveGravity * (rightHeight - left.x) / cellSpacing;
    float facePosition = float(cell + 1) / float(cellCount);
    // Ramp drag smoothly near both walls. Each neighboring cell computes exactly the same
    // face decay, preserving shared flux cancellation and zero net volume change.
    float wallDistance = min(facePosition, 1.0 - facePosition);
    float wallWeight = wallDampingWidth > 0.0
        ? 1.0 - smoothstep(0.0, wallDampingWidth, wallDistance) : 0.0;
    float decay = exp(-(damping + wallDamping * wallWeight) * timeStep);
    // Uniform lateral inertia moves liquid opposite acceleration. Vertical shaking also
    // seeds a zero-net-volume center/side disturbance; varying gravity alone cannot disturb a flat surface.
    // Three broad modes vary the shoulders and left/right balance. The absolute weights
    // sum to at most one, bounding input strength; all shapes vanish at both closed walls.
    float shape = 0.8 * sin(6.28318530718 * facePosition)
        + 0.12 * verticalShapeVariation * sin(3.14159265359 * facePosition)
        + (0.04 + 0.04 * verticalShapeVariation) * sin(9.42477796077 * facePosition);
    float shaking = -0.4 * containerAcceleration.y * shape;
    float acceleration = pressure - containerAcceleration.x + shaking;
    return (left.y + timeStep * acceleration) * decay;
}

/** Advances a conservative staggered linear shallow-water grid, one point per cell. */
void main()
{
    int cell = gl_VertexID;
    // The reference depth is one; height stores signed displacement about that reference.
    // Keep propagation speed independent of a landing/braking impulse. Vertical movement
    // still excites the surface through updatedFlow, without accelerating the wave clock.
    float effectiveGravity = gravity;
    float incoming = updatedFlow(cell - 1, effectiveGravity);
    nextFlow = updatedFlow(cell, effectiveGravity);
    // Every interior face is computed identically by its neighboring cells, so fluxes
    // telescope to zero total height change without clipping or a global reduction.
    nextHeight = texelFetch(state, cell).r + timeStep * (incoming - nextFlow) / cellSpacing;
    gl_Position = vec4(0.0, 0.0, 0.0, 1.0);
}
