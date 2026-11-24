/** What a fake media element plays: kept per element, read and written through HTMLMediaElement's own properties. */
interface MediaState {
  duration: number;
  currentTime: number;
  paused: boolean;
  seeking: boolean;
  ended: boolean;
  playbackRate: number;
  readyState: number;
  /** Bumped by every seek, so only the latest one ends the seeking. */
  seekGeneration: number;
  /** How often the code under test called play(): never by itself, there is no auto-play. */
  playCalls: number;
}

const states = new WeakMap<HTMLMediaElement, MediaState>();

function stateOf(element: HTMLMediaElement): MediaState {
  let state = states.get(element);
  if (state === undefined) {
    state = {
      duration: Number.NaN,
      currentTime: 0,
      paused: true,
      seeking: false,
      ended: false,
      playbackRate: 1,
      readyState: 0,
      seekGeneration: 0,
      playCalls: 0,
    };
    states.set(element, state);
  }
  return state;
}

const fire = (element: HTMLMediaElement, type: string) => element.dispatchEvent(new Event(type));

/**
 * A seek as the browser runs it, whoever asked (the controls by keyboard or pointer, or a script setting currentTime):
 * the time changes at once and `seeking` fires with `seeking` true; once it lands, `timeupdate` and `seeked` follow,
 * and `seeking` is false again, unless another seek started meanwhile.
 */
function seek(element: HTMLMediaElement, to: number) {
  const state = stateOf(element);
  state.seekGeneration += 1;
  const generation = state.seekGeneration;
  state.currentTime = to;
  state.seeking = true;
  state.ended = false;
  queueMicrotask(() => {
    fire(element, 'seeking');
    queueMicrotask(() => {
      if (state.seekGeneration !== generation) {
        return;
      }
      state.seeking = false;
      fire(element, 'timeupdate');
      fire(element, 'seeked');
    });
  });
}

/** The load algorithm, on a new source or load(): back to the start, paused, no metadata until loadMetadata(). */
function reset(element: HTMLMediaElement) {
  const state = stateOf(element);
  state.currentTime = 0;
  state.paused = true;
  state.seeking = false;
  state.ended = false;
  state.readyState = 0;
  state.duration = Number.NaN;
  queueMicrotask(() => fire(element, 'emptied'));
}

/**
 * jsdom plays nothing: its HTMLMediaElement has no duration, never moves its time, and play() and pause() are not
 * implemented. FakeMedia.install() puts a fake behind the prototype's properties for the test file, so the element the
 * component renders is the one the test drives, from the moment it is created: `FakeMedia.of(video)` loads its
 * metadata, plays and pauses it as the controls would, seeks as the controls or a script would, and moves its time on
 * in small steps, each with its `timeupdate`.
 */
export class FakeMedia {
  private readonly element: HTMLMediaElement;

  private constructor(element: HTMLMediaElement) {
    this.element = element;
  }

  static of(element: HTMLMediaElement): FakeMedia {
    return new FakeMedia(element);
  }

  static install(): void {
    const prototype = HTMLMediaElement.prototype;
    const nativeSrc = Object.getOwnPropertyDescriptor(prototype, 'src');
    const define = (name: string, descriptor: PropertyDescriptor) =>
      Object.defineProperty(prototype, name, { configurable: true, ...descriptor });

    define('duration', {
      get(this: HTMLMediaElement) {
        return stateOf(this).duration;
      },
    });
    define('currentTime', {
      get(this: HTMLMediaElement) {
        return stateOf(this).currentTime;
      },
      set(this: HTMLMediaElement, value: number) {
        seek(this, value);
      },
    });
    define('paused', {
      get(this: HTMLMediaElement) {
        return stateOf(this).paused;
      },
    });
    define('seeking', {
      get(this: HTMLMediaElement) {
        return stateOf(this).seeking;
      },
    });
    define('ended', {
      get(this: HTMLMediaElement) {
        return stateOf(this).ended;
      },
    });
    define('readyState', {
      get(this: HTMLMediaElement) {
        return stateOf(this).readyState;
      },
    });
    define('playbackRate', {
      get(this: HTMLMediaElement) {
        return stateOf(this).playbackRate;
      },
      set(this: HTMLMediaElement, value: number) {
        const state = stateOf(this);
        if (state.playbackRate !== value) {
          state.playbackRate = value;
          queueMicrotask(() => fire(this, 'ratechange'));
        }
      },
    });
    define('play', {
      value(this: HTMLMediaElement) {
        const state = stateOf(this);
        state.playCalls += 1;
        FakeMedia.of(this).play();
        return Promise.resolve();
      },
      writable: true,
    });
    define('pause', {
      value(this: HTMLMediaElement) {
        FakeMedia.of(this).pause();
      },
      writable: true,
    });
    define('load', {
      value(this: HTMLMediaElement) {
        reset(this);
      },
      writable: true,
    });
    define('src', {
      get(this: HTMLMediaElement) {
        return nativeSrc?.get?.call(this) ?? this.getAttribute('src') ?? '';
      },
      set(this: HTMLMediaElement, value: string) {
        const before = this.getAttribute('src');
        if (nativeSrc?.set !== undefined) {
          nativeSrc.set.call(this, value);
        } else {
          this.setAttribute('src', value);
        }
        if (before !== null && before !== this.getAttribute('src')) {
          reset(this);
        }
      },
    });
  }

  get currentTime(): number {
    return stateOf(this.element).currentTime;
  }

  /** Whether a seek is still on its way: it lands a moment after it started. */
  get seeking(): boolean {
    return stateOf(this.element).seeking;
  }

  get paused(): boolean {
    return stateOf(this.element).paused;
  }

  get playbackRate(): number {
    return stateOf(this.element).playbackRate;
  }

  /** How often the code under test called play() on the element. */
  get playCalls(): number {
    return stateOf(this.element).playCalls;
  }

  /** The file's metadata arrives: its own duration (which may differ from the editor's), then `loadedmetadata`. */
  loadMetadata(duration: number): void {
    const state = stateOf(this.element);
    state.duration = duration;
    state.readyState = 1;
    fire(this.element, 'loadedmetadata');
  }

  /** Playback starts, as the play button or play() starts it. */
  play(): void {
    const state = stateOf(this.element);
    if (!state.paused) {
      return;
    }
    state.paused = false;
    state.ended = false;
    fire(this.element, 'play');
    fire(this.element, 'playing');
  }

  /** Playback pauses, as the pause button or pause() pauses it. */
  pause(): void {
    const state = stateOf(this.element);
    if (state.paused) {
      return;
    }
    state.paused = true;
    fire(this.element, 'pause');
  }

  /**
   * Plays on for the seconds in steps of a quarter second, each with its `timeupdate`; at the file's end playback stops
   * as the browser stops it: `timeupdate`, `pause`, then `ended`.
   */
  playFor(seconds: number): void {
    const state = stateOf(this.element);
    const until = state.currentTime + seconds;
    while (!state.paused && state.currentTime < until) {
      state.currentTime = Math.min(until, state.currentTime + 0.25, state.duration);
      fire(this.element, 'timeupdate');
      if (state.currentTime >= state.duration) {
        state.paused = true;
        state.ended = true;
        fire(this.element, 'pause');
        fire(this.element, 'ended');
      }
    }
  }

  /** A seek by the controls (keyboard or pointer): the time the learner asked for. */
  seekTo(to: number): void {
    seek(this.element, to);
  }

  /** The time leaps without a seek, as a glitch or a script's trick would: only `timeupdate` says so. */
  leapTo(to: number): void {
    stateOf(this.element).currentTime = to;
    fire(this.element, 'timeupdate');
  }

  /** The source fails to play: `error`. */
  fail(): void {
    fire(this.element, 'error');
  }
}
