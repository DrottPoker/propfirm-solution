// A market's closed days, in its own local time as the configuration writes them, for example "2026-12-24T13:15:00".

const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

type Local = { year: number; month: number; day: number; hour: number; minute: number };

function parse(value: string): Local {
  const [date, time = "00:00"] = value.split("T");
  const [year, month, day] = date.split("-").map(Number);
  const [hour, minute] = time.split(":").map(Number);
  return { year, month, day, hour, minute };
}

const pad = (value: number) => String(value).padStart(2, "0");
const dayText = (t: Local) => `${t.day} ${months[t.month - 1]} ${t.year}`;
const shortDay = (t: Local) => `${t.day} ${months[t.month - 1]}`;
const timeText = (t: Local) => `${pad(t.hour)}:${pad(t.minute)}`;
const midnight = (t: Local) => t.hour === 0 && t.minute === 0;
const dayNumber = (t: Local) => Date.UTC(t.year, t.month - 1, t.day) / 86_400_000;

/** The city of a time zone, for example "New York" for America/New_York. */
export function zoneCity(timeZone: string): string {
  return (timeZone.split("/").pop() ?? timeZone).replaceAll("_", " ");
}

/**
 * A closure in words, in the market's own time: "25 Dec 2026" for a whole day, "24 Dec 2026, 13:15 to 24:00" for part of
 * one, and "24 Dec 13:15 to 27 Dec 18:00" across days.
 */
export function closureText(from: string, to: string): string {
  const start = parse(from);
  const end = parse(to);
  const days = dayNumber(end) - dayNumber(start);
  if (midnight(start) && midnight(end)) {
    return days <= 1 ? dayText(start) : `${shortDay(start)} to ${dayText(parse(new Date((dayNumber(end) - 1) * 86_400_000).toISOString().slice(0, 10)))}`;
  }

  if (days === 0) {
    return `${dayText(start)}, ${timeText(start)} to ${timeText(end)}`;
  }

  if (days === 1 && midnight(end)) {
    return `${dayText(start)}, ${timeText(start)} to 24:00`;
  }

  return `${shortDay(start)} ${timeText(start)} to ${shortDay(end)} ${timeText(end)}`;
}
