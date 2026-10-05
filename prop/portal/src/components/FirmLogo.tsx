/**
 * A firm's logo at the height its class name gives it, as wide as its own shape makes it. A plain img, since
 * next/image needs the logo's size, which is not known: logos are any shape, and on the firms' own addresses, so they
 * are not optimized anyway.
 */
export function FirmLogo({ src, alt, className }: { src: string; alt: string; className: string }) {
  // eslint-disable-next-line @next/next/no-img-element -- a firm's logo of unknown size, which next/image would need
  return <img src={src} alt={alt} className={`w-auto object-contain ${className}`} />;
}
