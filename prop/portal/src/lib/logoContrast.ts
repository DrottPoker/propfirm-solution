import { contrastRatio } from "./theme";

/** Below this contrast with the background, a logo is said to be hard to see. Logos are shapes, not text, so it is low. */
export const logoContrast = 1.5;

/**
 * The luminance of the image's visible pixels on average, from 0 for black to 1 for white, as WCAG counts it. Pixels
 * that are mostly transparent are left out. Null when the image cannot be read, for example from another address.
 */
export async function imageLuminance(src: string): Promise<number | null> {
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
    return averageLuminance(context.getImageData(0, 0, canvas.width, canvas.height).data);
  } catch {
    return null;
  }
}

/** The average luminance of RGBA pixels, weighted by how visible each is. Null without visible pixels. */
export function averageLuminance(pixels: ArrayLike<number>): number | null {
  let total = 0;
  let weight = 0;
  for (let i = 0; i + 3 < pixels.length; i += 4) {
    const alpha = pixels[i + 3] / 255;
    if (alpha < 0.1) {
      continue;
    }

    const [r, g, b] = [pixels[i], pixels[i + 1], pixels[i + 2]].map((v) => {
      const c = v / 255;
      return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
    });
    total += (0.2126 * r + 0.7152 * g + 0.0722 * b) * alpha;
    weight += alpha;
  }

  return weight > 0 ? total / weight : null;
}

/** Whether a logo of this luminance is hard to see on the background color. */
export function logoHardToSee(luminance: number, background: string): boolean {
  const gray = Math.round(255 * (luminance <= 0.0031308 ? luminance * 12.92 : 1.055 * luminance ** (1 / 2.4) - 0.055));
  const hex = `#${Math.min(255, Math.max(0, gray)).toString(16).padStart(2, "0").repeat(3)}`;
  return contrastRatio(hex, background) < logoContrast;
}
