// Alarm sound via the Web Audio API (no audio files). Browsers block audio until a user gesture, so the
// dashboard only calls enable() after the operator switched the sound on ("Ton an").

let ctx = null;
let enabled = false;
let busyUntil = 0; // AudioContext time until which a beep sequence is playing (no overlapping alarms)

const patterns = {
  nok: { freq: 880, count: 2 },
  danger: { freq: 600, count: 3 },
};

/** Creates/resumes the AudioContext. Returns true if audio is running. */
export async function enable() {
  try {
    if (!ctx) {
      const Ctor = window.AudioContext || window.webkitAudioContext;
      if (!Ctor) {
        return false;
      }
      ctx = new Ctor();
    }
    if (ctx.state === "suspended") {
      await ctx.resume();
    }
    enabled = true;
    return ctx.state === "running";
  } catch {
    return false;
  }
}

/** Mutes the alarm sound. */
export function disable() {
  enabled = false;
}

/** Plays the beep pattern for "nok" (2 × 880 Hz) or "danger" (3 × 600 Hz). */
export function beep(kind) {
  if (!enabled || !ctx || ctx.state !== "running") {
    return;
  }
  const p = patterns[kind];
  if (!p || ctx.currentTime < busyUntil) {
    return;
  }

  const beepLen = 0.15;
  const gap = 0.1;
  const start = ctx.currentTime + 0.02;
  for (let i = 0; i < p.count; i++) {
    const t = start + i * (beepLen + gap);
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.type = "square";
    osc.frequency.value = p.freq;
    gain.gain.setValueAtTime(0.0001, t);
    gain.gain.exponentialRampToValueAtTime(0.25, t + 0.01);
    gain.gain.exponentialRampToValueAtTime(0.0001, t + beepLen);
    osc.connect(gain).connect(ctx.destination);
    osc.start(t);
    osc.stop(t + beepLen + 0.02);
  }
  busyUntil = start + p.count * (beepLen + gap);
}
