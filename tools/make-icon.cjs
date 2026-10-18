// Usage: node tools/make-icon.cjs [path-to-sharp]
// The generated ICO is checked in; ordinary application builds need no Node.js.
const fs = require('node:fs/promises');
const path = require('node:path');
const sharp = require(process.argv[2] || 'sharp');
(async () => {
  const root = path.resolve(__dirname, '..');
  const source = await fs.readFile(path.join(root, 'src', 'app-icon.png'));
  const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
  const frames = await Promise.all(sizes.map(size => sharp(source).resize(size, size).png().toBuffer()));
  const header = Buffer.alloc(6 + 16 * sizes.length);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(sizes.length, 4);
  let offset = header.length;
  frames.forEach((frame, i) => {
    const pos = 6 + i * 16;
    header[pos] = header[pos + 1] = sizes[i] === 256 ? 0 : sizes[i];
    header.writeUInt16LE(1, pos + 4);
    header.writeUInt16LE(32, pos + 6);
    header.writeUInt32LE(frame.length, pos + 8);
    header.writeUInt32LE(offset, pos + 12);
    offset += frame.length;
  });
  await fs.writeFile(path.join(root, 'src', 'app.ico'), Buffer.concat([header, ...frames]));
  console.log('Created src/app.ico: ' + sizes.join(', ') + ' px');
})().catch(error => { console.error(error); process.exitCode = 1; });
