#version 330 core
uniform samplerBuffer state;
uniform int particleCount;
uniform int pass;
uniform float timeStep;
uniform vec2 containerAcceleration;
out vec4 nextPositionVelocity;
out vec4 nextPreviousRadius;

/* Executes prediction, relaxed Jacobi contacts, or velocity reconstruction for one grain. */
void main()
{
    int i = gl_VertexID;
    vec4 pv = texelFetch(state, 2*i);
    vec4 pr = texelFetch(state, 2*i+1);
    vec2 p = pv.xy;
    float r = pr.z;
    if (pass == 0)
    {
        pr.xy = p;
        // Strong settling and drag keep chunks in a compact pile rather than freely tumbling like food grains.
        pv.zw += (vec2(0.0, -6.0) - containerAcceleration) * timeStep;
        // A restrained, identity-stable shake releases a few pieces from the soft contact bed.
        // Drawing uses a fixed volume envelope, so these hops cannot compress unrelated pieces.
        uint hash = uint(i) + 0x9e3779b9u;
        hash = (hash ^ (hash >> 16u)) * 0x85ebca6bu;
        hash = (hash ^ (hash >> 13u)) * 0xc2b2ae35u;
        float kick = float((hash ^ (hash >> 16u)) >> 8u) / 16777216.0;
        // Spread a smaller response across pieces instead of concentrating strong launches in a rare few.
        pv.w += max(0.0, containerAcceleration.y) * 2.8 * mix(0.75, 1.0, kick) * timeStep;
        pv.zw *= exp(-5.0*timeStep);
        pv.zw /= max(1.0, length(pv.zw)/1.2);
        // Metal lifts slowly but falls freely; keep strong pitch impulses from launching pieces high.
        pv.w = min(pv.w, 0.25);
        p += pv.zw*timeStep;
    }
    else if (pass == 1)
    {
        vec2 correction = vec2(0.0);
        for (int j = 0; j < particleCount; ++j)
        {
            if (j == i) continue;
            vec4 other = texelFetch(state, 2*j);
            vec2 delta = p - other.xy;
            // The solid-metal profile caps radius at .035; keep the broad-phase bound conservative.
            if (dot(delta, delta) > (r+0.036)*(r+0.036)) continue;
            vec4 otherPr = texelFetch(state, 2*j+1);
            float distance = length(delta);
            float overlap = r + otherPr.z - distance;
            if (overlap <= 0.0) continue;
            // Coincident centers receive opposite deterministic normals, avoiding division by zero.
            vec2 n = distance > 0.000001 ? delta/distance : vec2(i < j ? -1.0 : 1.0, 0.0);
            vec2 tangent = vec2(-n.y, n.x);
            float slip = dot((p-pr.xy)-(other.xy-otherPr.xy), tangent);
            // Static friction cancels small tangential displacement; dynamic friction limits larger slips.
            // Drag supplies the heavy settling response; contact friction must still allow ordinary camera impulses to shift chunks.
            float friction = abs(slip) < 0.55*overlap ? slip : clamp(slip, -0.4*overlap, 0.4*overlap);
            correction += 0.5*(overlap*n - friction*tangent);
        }
        p += 0.65*correction;
        vec2 clamped = clamp(p, vec2(r), vec2(1.0-r));
        vec2 penetration = clamped-p;
        if (abs(penetration.y) > 0.0)
            clamped.x -= clamp(p.x-pr.x, -0.55*abs(penetration.y), 0.55*abs(penetration.y));
        if (abs(penetration.x) > 0.0)
            clamped.y -= clamp(p.y-pr.y, -0.55*abs(penetration.x), 0.55*abs(penetration.x));
        p = clamp(clamped, vec2(r), vec2(1.0-r));
    }
    else
    {
        // Estimate the resting envelope from conserved circle area at a loose packing fraction.
        // Unlike the tallest airborne piece, this reference cannot stretch or squeeze the whole pile.
        float circleArea = 0.0;
        for (int j = 0; j < particleCount; ++j)
        {
            float radius = texelFetch(state, 2*j+1).z;
            circleArea += 3.14159265 * radius * radius;
        }
        pr.w = circleArea / 0.675;
        pv.zw = (p-pr.xy)/timeStep;
        // Closed walls are inelastic: remove outward velocity after positional projection.
        if (p.x <= r+0.00001) pv.z = max(0.0, pv.z);
        if (p.x >= 1.0-r-0.00001) pv.z = min(0.0, pv.z);
        if (p.y <= r+0.00001) pv.w = max(0.0, pv.w);
        if (p.y >= 1.0-r-0.00001) pv.w = min(0.0, pv.w);
        pv.zw /= max(1.0, length(pv.zw)/1.2);
        pv.w = min(pv.w, 0.25);
    }
    nextPositionVelocity = vec4(p, pv.zw);
    nextPreviousRadius = pr;
    gl_Position = vec4(0.0);
}
