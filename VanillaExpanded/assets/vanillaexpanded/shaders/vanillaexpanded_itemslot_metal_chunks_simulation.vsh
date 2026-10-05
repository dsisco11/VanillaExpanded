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
        // Soft inelastic contacts otherwise turn vertical impacts only into invisible compression.
        // Redistribute a small part of the compressive shake into varied upward collision-like kicks.
        // Cubing the stable sample gives most chunks little lift and a few visible hops, with no idle/fall forcing.
        uint hash = uint(i) + 0x9e3779b9u;
        hash = (hash ^ (hash >> 16u)) * 0x85ebca6bu;
        hash = (hash ^ (hash >> 13u)) * 0xc2b2ae35u;
        float kick = float((hash ^ (hash >> 16u)) >> 8u) / 16777216.0;
        pv.w += max(0.0, containerAcceleration.y) * 5.0 * kick * kick * kick * timeStep;
        pv.zw *= exp(-5.0*timeStep);
        pv.zw /= max(1.0, length(pv.zw)/1.2);
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
        // Publish a common pile height once per substep, avoiding reductions in every slot draw.
        float pileTop = 0.0;
        for (int j = 0; j < particleCount; ++j)
            pileTop = max(pileTop, texelFetch(state, 2*j).y + texelFetch(state, 2*j+1).z);
        pr.w = pileTop;
        pv.zw = (p-pr.xy)/timeStep;
        // Closed walls are inelastic: remove outward velocity after positional projection.
        if (p.x <= r+0.00001) pv.z = max(0.0, pv.z);
        if (p.x >= 1.0-r-0.00001) pv.z = min(0.0, pv.z);
        if (p.y <= r+0.00001) pv.w = max(0.0, pv.w);
        if (p.y >= 1.0-r-0.00001) pv.w = min(0.0, pv.w);
        pv.zw /= max(1.0, length(pv.zw)/1.2);
    }
    nextPositionVelocity = vec4(p, pv.zw);
    nextPreviousRadius = pr;
    gl_Position = vec4(0.0);
}
