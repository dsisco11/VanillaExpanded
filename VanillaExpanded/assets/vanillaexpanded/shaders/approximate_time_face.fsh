#version 330 core
uniform sampler2D housing;
uniform sampler2D dayScenery;
uniform sampler2D nightScenery;
uniform vec4 faceGeometry;
uniform float segmentInset;
uniform vec4 hourColors[24];
uniform vec4 solarSamples[72];
out vec4 fragColor;

/* Converts an authored straight-alpha texture sample to premultiplied color. */
vec4 premultiply(vec4 color)
{
    return vec4(color.rgb * color.a, color.a);
}

/* Masks seasonal artwork and composes the complete static clock background in one GPU pass. */
void main()
{
    const float pi = 3.141592653589793;
    // Texture row zero is the authored top of the GUI image, including for this render target.
    vec2 point = gl_FragCoord.xy;
    vec2 uv = point / faceGeometry.w;
    vec2 delta = point - faceGeometry.xy;
    float radius = length(delta);
    float hourPosition = mod((atan(delta.y, delta.x) + pi / 2.0) * 12.0 / pi + 12.0, 24.0);
    float segmentPosition = fract(hourPosition + 0.5);
    int hour = int(floor(mod(hourPosition + 0.5, 24.0)));
    int sampleIndex = hour * 12 + min(11, int(floor(segmentPosition * 12.0)));
    float solar = solarSamples[sampleIndex / 4][sampleIndex % 4];

    // A pixel-wide transition softens the aperture and the narrow gaps between hourly wedges.
    float edgeAngle = min(segmentPosition, 1.0 - segmentPosition) * pi / 12.0;
    float angleWidth = min(pi / 24.0, 1.0 / max(radius, 1.0));
    float dividers = smoothstep(segmentInset - angleWidth * 0.5, segmentInset + angleWidth * 0.5, edgeAngle);
    float aperture = 1.0 - smoothstep(faceGeometry.z - 0.5, faceGeometry.z + 0.5, radius);
    float coverage = aperture * dividers;

    // Sample before selection so implicit mip derivatives remain valid across solar boundaries.
    vec4 day = texture(dayScenery, uv);
    vec4 night = texture(nightScenery, uv);
    vec4 scene = solar == 1.0 ? day : solar == 2.0 ? night : vec4(0.0);
    vec3 face = mix(hourColors[hour].rgb, scene.rgb, scene.a);
    vec4 backing = premultiply(texture(housing, uv));
    fragColor = vec4(face * coverage, coverage) + backing * (1.0 - coverage);
}
