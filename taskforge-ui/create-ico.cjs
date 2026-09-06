// Script to convert PNG to Windows ICO format using sharp
const sharp = require('sharp');
const fs = require('fs');
const path = require('path');

const inputPng = path.join(__dirname, '..', 'docs', 'logo.png');
const outputIco = path.join(__dirname, '..', 'installer', 'icon.ico');

// Ensure installer directory exists
const installerDir = path.dirname(outputIco);
if (!fs.existsSync(installerDir)) {
  fs.mkdirSync(installerDir, { recursive: true });
}

// ICO file header structure
function createIcoBuffer(images) {
  const headerSize = 6;
  const entrySize = 16;
  const numImages = images.length;
  
  // Calculate total size
  let totalSize = headerSize + (entrySize * numImages);
  const offsets = [];
  
  for (const img of images) {
    offsets.push(totalSize);
    totalSize += img.size;
  }
  
  // Create buffer
  const buffer = Buffer.alloc(totalSize);
  
  // Write header
  buffer.writeUInt16LE(0, 0); // Reserved
  buffer.writeUInt16LE(1, 2); // Type: 1 = ICO
  buffer.writeUInt16LE(numImages, 4); // Number of images
  
  // Write directory entries
  let offset = headerSize;
  for (let i = 0; i < images.length; i++) {
    const img = images[i];
    const size = img.width > 255 ? 0 : img.width;
    buffer.writeUInt8(size, offset); // Width
    buffer.writeUInt8(size, offset + 1); // Height
    buffer.writeUInt8(0, offset + 2); // Color palette
    buffer.writeUInt8(0, offset + 3); // Reserved
    buffer.writeUInt16LE(1, offset + 4); // Color planes
    buffer.writeUInt16LE(32, offset + 6); // Bits per pixel
    buffer.writeUInt32LE(img.size, offset + 8); // Image size
    buffer.writeUInt32LE(offsets[i], offset + 12); // Image offset
    offset += entrySize;
  }
  
  // Write image data
  for (let i = 0; i < images.length; i++) {
    images[i].data.copy(buffer, offsets[i]);
  }
  
  return buffer;
}

async function convert() {
  try {
    const pngBuffer = fs.readFileSync(inputPng);
    
    // Get image metadata
    const metadata = await sharp(pngBuffer).metadata();
    console.log(`Source: ${metadata.width}x${metadata.height} PNG`);
    
    // Create different sizes for ICO
    const sizes = [256, 128, 64, 48, 32, 16];
    const images = [];
    
    for (const size of sizes) {
      // Resize and convert to PNG buffer
      const resizedBuffer = await sharp(pngBuffer)
        .resize(size, size, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
        .png()
        .toBuffer();
      
      images.push({
        width: size,
        height: size,
        size: resizedBuffer.length,
        data: resizedBuffer
      });
      
      console.log(`  - ${size}x${size}: ${(resizedBuffer.length / 1024).toFixed(2)} KB`);
    }
    
    // Create ICO file
    const icoBuffer = createIcoBuffer(images);
    fs.writeFileSync(outputIco, icoBuffer);
    
    console.log(`\n✓ Created icon.ico (${(icoBuffer.length / 1024).toFixed(2)} KB)`);
    console.log(`  Path: ${outputIco}`);
  } catch (err) {
    console.error('Error converting icon:', err);
    process.exit(1);
  }
}

convert();