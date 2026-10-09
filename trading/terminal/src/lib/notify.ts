import { useSettings } from "./settings";

// The computer's own notifications (ADR 0058), for what happens while the terminal is in the background: a fill, a
// close, a price alert or a warning about the rules. Only when the trader turned them on in Settings and the browser
// allows them. While the terminal is in front, its own notes say the same.

/** Whether this browser can show notifications at all. */
export function notificationsSupported(): boolean {
  return typeof window !== "undefined" && "Notification" in window;
}

/** Asks the browser for leave to show notifications, once the trader turned them on. True when allowed. */
export async function askForNotifications(): Promise<boolean> {
  if (!notificationsSupported()) {
    return false;
  }

  if (Notification.permission === "granted") {
    return true;
  }

  return Notification.permission !== "denied" && (await Notification.requestPermission()) === "granted";
}

/** Shows the notification when the terminal is in the background, notifications are on and the browser allows them. */
export function notifyInBackground(title: string, body?: string) {
  if (!notificationsSupported() || !document.hidden || !useSettings.getState().notifications || Notification.permission !== "granted") {
    return;
  }

  try {
    // The same title replaces the one before, so a burst of fills does not pile up.
    new Notification(title, { body, icon: "/icon.svg", tag: title });
  } catch {
    // Some browsers only show notifications from a service worker; the note in the terminal still tells it.
  }
}
