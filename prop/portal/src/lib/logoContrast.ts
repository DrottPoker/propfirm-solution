import { contrastRatio } from "./theme";

/** Below this contrast with the background, a part of a logo is said to be hard to see. Logos are shapes, not text, so it is low. */
export const logoContrast = 1.5;

/**
 * The share of a logo's outline that may blend into the background before the logo is said to be hard to see: enough to
 * catch white text beside a colored mark on a light theme, little enough that a few soft edge pixels do not count.
 */
export const hardToSeeShare = 0.3;

/**
 * The luminance of the pixels on the logo's outline, where the logo meets the background, from 0 for black to 1 for
 * white, as WCAG counts it. Null when the image cannot be read, for example from another address, or when it has no
 * transparent pixels, so its own background is what meets the portal's.
 */
export async function imageEdges(src: string): Promise<number[] | null> {
  const image = new Image();
  image.crossOrigin = "anonymous";
  image.src = src;
  try {
    await image.decode();
    const canvas = document.createElement("canvas");
    const scale = Math.min(1, 200 / Math.max(image.naturalWidth || 1, image.naturalHeight || 1));
    canvas.width = Math.max(1, Math.round((image.naturalWidth || 200) * scale));
    canvas.height = Math.max(1, Math.round((image.naturalHeight || 50) * scale));
    const context = canvas.getContext("2d");
    if (!context) {
      return null;
    }

    context.drawImage(image, 0, 0, canvas.width, canvas.height);
    return edgeLuminances(context.getImageData(0, 0, canvas.width, canvas.height).data, canvas.width, canvas.height);
  } catch {
    return null;
  }
}

/**
 * The luminance of every visible RGBA pixel next to a transparent one, or to the image's edge, which is where the
 * background shows. Details inside the logo, such as white text on its own colored shape, are not on the outline and
 * do not count. Null for an image without transparent pixels, or without visible ones.
 */
export function edgeLuminances(pixels: ArrayLike<number>, width: number, height: number): number[] | null {
  const visible = (x: number, y: number) => x >= 0 && y >= 0 && x < width && y < height && pixels[(y * width + x) * 4 + 3] >= 128;
  let transparent = false;
  for (let i = 3; i < pixels.length; i += 4) {
    if (pixels[i] < 128) {
      transparent = true;
      break;
    }
  }

  if (!transparent) {
    return null;
  }

  const edges: number[] = [];
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      if (visible(x, y) && (!visible(x - 1, y) || !visible(x + 1, y) || !visible(x, y - 1) || !visible(x, y + 1))) {
        const i = (y * width + x) * 4;
        edges.push(luminanceOf(pixels[i], pixels[i + 1], pixels[i + 2]));
      }
    }
  }

  return edges.length > 0 ? edges : null;
}

/** Whether a logo with this outline is hard to see on the background color: a good part of its outline blends into it. */
export function logoHardToSee(edges: readonly number[], background: string): boolean {
  if (edges.length === 0) {
    return false;
  }

  const blending = edges.filter((luminance) => contrastRatio(grayOf(luminance), background) < logoContrast).length;
  return blending / edges.length >= hardToSeeShare;
}

function luminanceOf(red: number, green: number, blue: number): number {
  const [r, g, b] = [red, green, blue].map((v) => {
    const c = v / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

// A gray as light as the luminance, as a color contrastRatio takes.
function grayOf(luminance: number): string {
  const gray = Math.round(255 * (luminance <= 0.0031308 ? luminance * 12.92 : 1.055 * luminance ** (1 / 2.4) - 0.055));
  return `#${Math.min(255, Math.max(0, gray)).toString(16).padStart(2, "0").repeat(3)}`;
}
