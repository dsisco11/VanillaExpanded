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
        pv.zw += (vec2(0.0, -4.0) - containerAcceleration) * timeStep;
        pv.zw *= exp(-0.35*timeStep);
        pv.zw /= max(1.0, length(pv.zw)/3.0);
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
            // Initialization caps radius below .024; distant grains need no metadata fetch or square root.
            if (dot(delta, delta) > (r+0.024)*(r+0.024)) continue;
            vec4 otherPr = texelFetch(state, 2*j+1);
            float distance = length(delta);
            float overlap = r + otherPr.z - distance;
            if (overlap <= 0.0) continue;
            // Coincident centers receive opposite deterministic normals, avoiding division by zero.
            vec2 n = distance > 0.000001 ? delta/distance : vec2(i < j ? -1.0 : 1.0, 0.0);
            vec2 tangent = vec2(-n.y, n.x);
            float slip = dot((p-pr.xy)-(other.xy-otherPr.xy), tangent);
            // Static friction cancels small tangential displacement; dynamic friction limits larger slips.
            float friction = abs(slip) < 0.65*overlap ? slip : clamp(slip, -0.45*overlap, 0.45*overlap);
            correction += 0.5*(overlap*n - friction*tangent);
        }
        p += 0.65*correction;
        vec2 clamped = clamp(p, vec2(r), vec2(1.0-r));
        vec2 penetration = clamped-p;
        if (abs(penetration.y) > 0.0)
            clamped.x -= clamp(p.x-pr.x, -0.65*abs(penetration.y), 0.65*abs(penetration.y));
        if (abs(penetration.x) > 0.0)
            clamped.y -= clamp(p.y-pr.y, -0.65*abs(penetration.x), 0.65*abs(penetration.x));
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
        pv.zw /= max(1.0, length(pv.zw)/3.0);
    }
    nextPositionVelocity = vec4(p, pv.zw);
    nextPreviousRadius = pr;
    gl_Position = vec4(0.0);
}
