// A short chime when an order fills, made with the Web Audio API, so there is no sound file to load.

let context: AudioContext | null = null;

function audio(): AudioContext | null {
  try {
    context ??= new AudioContext();
    return context;
  } catch {
    // No Web Audio in this browser: the terminal stays silent.
    return null;
  }
}

/** Prepares sound while the trader clicks, since browsers only start audio after a click or a key press. */
export function unlockSound() {
  void audio()?.resume().catch(() => undefined);
}

/** Two quick, soft tones a fifth apart, rising: an order filled. */
export function playFillSound() {
  const ctx = audio();
  if (!ctx) {
    return;
  }

  if (ctx.state === "suspended") {
    void ctx.resume().catch(() => undefined);
  }

  const start = ctx.currentTime + 0.01;
  [880, 1320].forEach((frequency, i) => {
    const at = start + i * 0.08;
    const tone = ctx.createOscillator();
    const volume = ctx.createGain();
    tone.type = "sine";
    tone.frequency.value = frequency;
    volume.gain.setValueAtTime(0.0001, at);
    volume.gain.exponentialRampToValueAtTime(0.12, at + 0.012);
    volume.gain.exponentialRampToValueAtTime(0.0001, at + 0.16);
    tone.connect(volume).connect(ctx.destination);
    tone.start(at);
    tone.stop(at + 0.18);
  });
}
