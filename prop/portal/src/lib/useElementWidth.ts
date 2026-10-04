import { useEffect, useState } from "react";

/**
 * A ref for an element and its width in pixels, kept up to date as it changes. The width is 0 until the element is
 * shown and measured, also when it is shown later than the component.
 */
export function useElementWidth<T extends HTMLElement>(): { ref: (element: T | null) => void; element: T | null; width: number } {
  const [element, setElement] = useState<T | null>(null);
  const [width, setWidth] = useState(0);

  useEffect(() => {
    if (!element) {
      return;
    }

    const observer = new ResizeObserver(([entry]) => setWidth(Math.floor(entry.contentRect.width)));
    observer.observe(element);
    return () => observer.disconnect();
  }, [element]);

  return { ref: setElement, element, width };
}
