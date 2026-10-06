// Short sounds made with the Web Audio API, so there is no sound file to load: a chime when an order fills and a
// signal with a warning about the account's rules.

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
  playTones([880, 1320], { gap: 0.08, length: 0.16, wave: "sine", volume: 0.12 });
}

/** Two longer tones falling a fifth, a little louder: a rule needs the trader's attention. */
export function playWarningSound() {
  playTones([784, 523], { gap: 0.16, length: 0.26, wave: "triangle", volume: 0.18 });
}

function playTones(frequencies: readonly number[], { gap, length, wave, volume: peak }: { gap: number; length: number; wave: OscillatorType; volume: number }) {
  const ctx = audio();
  if (!ctx) {
    return;
  }

  if (ctx.state === "suspended") {
    void ctx.resume().catch(() => undefined);
  }

  const start = ctx.currentTime + 0.01;
  frequencies.forEach((frequency, i) => {
    const at = start + i * gap;
    const tone = ctx.createOscillator();
    const volume = ctx.createGain();
    tone.type = wave;
    tone.frequency.value = frequency;
    volume.gain.setValueAtTime(0.0001, at);
    volume.gain.exponentialRampToValueAtTime(peak, at + 0.012);
    volume.gain.exponentialRampToValueAtTime(0.0001, at + length);
    tone.connect(volume).connect(ctx.destination);
    tone.start(at);
    tone.stop(at + length + 0.02);
  });
}
