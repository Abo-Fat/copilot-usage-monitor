const fs = require("node:fs");
const path = require("node:path");
const assert = require("node:assert/strict");
const { Resvg } = require("@resvg/resvg-js");

const root = path.resolve(__dirname, "..", "..");
const icons = path.join(root, "icons");
const output = path.join(root, "src", "CopilotUsage.Tray", "Assets");
const sizes = [16, 20, 24, 32, 40, 48, 64];
const names = fs.readdirSync(icons);
const read = suffix => {
    const matches = names.filter(name => name.endsWith(suffix));
    assert.equal(matches.length, 1, `Expected one source SVG ending in ${suffix}`);
    return fs.readFileSync(path.join(icons, matches[0]), "utf8");
};
const full = read("_battery-full.svg");
const empty = read("_battery-empty.svg");
const bars = [...full.matchAll(/<path\b[^>]*\bd="M(?:13|19|25|31) 21V27"[^>]*\/>/g)];
assert.equal(bars.length, 4, "Source full-battery SVG must have four separate vertical bars");
fs.mkdirSync(output, { recursive: true });

function textChunk(keyword, value) {
    const text = Buffer.from(`${keyword}\0${value}`, "latin1");
    const chunk = Buffer.alloc(text.length + 12);
    chunk.writeUInt32BE(text.length, 0);
    chunk.write("tEXt", 4, 4, "ascii");
    text.copy(chunk, 8);
    // PNG checksums cover the chunk type and data, but not the length field.
    let crc = 0xffffffff;
    for (const byte of chunk.subarray(4, 8 + text.length)) {
        crc ^= byte;
        for (let bit = 0; bit < 8; bit++)
            crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
    }
    chunk.writeUInt32BE((~crc) >>> 0, 8 + text.length);
    return chunk;
}

function withAttribution(png, level, size) {
    assert.equal(png.toString("ascii", png.length - 8, png.length - 4), "IEND");
    return Buffer.concat([
        png.subarray(0, -12),
        textChunk("Copyright", "Copyright 2019-present Bytedance Inc."),
        textChunk("Source", "https://github.com/bytedance/IconPark/tree/8dc132da4c85671ba6a5962c87aa2bdafbf158e9"),
        textChunk("License", "Apache-2.0; see licenses\\IconPark-LICENSE.txt"),
        textChunk("Description", `Modified by Abo-Fat from IconPark battery-full/battery-empty: ${level} charge bars, white foreground, rasterized at ${size}x${size}.`),
        png.subarray(-12)
    ]);
}

for (let level = 0; level <= 4; level++) {
    let svg = level === 0 ? empty : full;
    if (level > 0) {
        for (const bar of bars.slice(level)) svg = svg.replace(bar[0], "");
    }
    svg = svg.replace(/#333\b/g, "#ffffff");
    for (const size of sizes) {
        const png = new Resvg(svg, { fitTo: { mode: "width", value: size } }).render();
        assert.equal(png.width, size);
        assert.equal(png.height, size);
        fs.writeFileSync(path.join(output, `battery-${level}-${size}.png`), withAttribution(png.asPng(), level, size));
    }
}
console.log(`Generated ${5 * sizes.length} transparent battery assets from the supplied SVGs.`);
