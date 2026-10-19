#version 330 core
layout(location=0) in vec3 vertex;
layout(location=1) in vec2 uv;
uniform vec4 screenBounds;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
out vec2 habitatUv;
/** Places a reusable quad in the caller-provided map rectangle. */
void main() {
    habitatUv = uv;
    gl_Position = projectionMatrix * modelViewMatrix * vec4(screenBounds.xy + (vertex.xy * 0.5 + 0.5) * screenBounds.zw, 55.0, 1.0);
}
