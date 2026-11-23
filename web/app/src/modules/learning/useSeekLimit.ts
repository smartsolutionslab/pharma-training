import { type MaybeRefOrGetter, toValue } from 'vue';

/** The longest step between two timeupdates that still counts as playing; a longer one is a leap. */
const playingStep = 2;
/** How far past the watched maximum a seek may land without being refused. */
const seekTolerance = 0.5;

export interface SeekLimit {
  /** The furthest point, in seconds, watched by playing. */
  maximum: () => number;
  /** Raises the maximum to a point watched elsewhere (the server's progress, from this or another device). */
  raise: (seconds: number) => void;
  /** The playhead is placed here (a resume or a new source): the next timeupdate is measured from it. */
  placeAt: (seconds: number) => void;
  /** A timeupdate while not seeking: the maximum rises only by steps of playing, never by a leap. */
  played: (seconds: number) => void;
  /** A seek to the point: where it must go back to when it is refused, else undefined. */
  seek: (seconds: number) => number | undefined;
}

/**
 * The seek limit of a video chapter (#44): skipping ahead stops at what was watched. The watched maximum rises on
 * timeupdate only while the time moves on by at most 2 s from the last one, so a leap without a seek counts for
 * nothing; the steps after it are measured from where it landed, so a gap in timeupdates (a stalled or throttled tab)
 * never stops the maximum rising. A seek more than 0.5 s past it is sent back to it; backwards is free, and so is
 * everything while `free` (a chapter done).
 */
export function useSeekLimit(free: MaybeRefOrGetter<boolean>): SeekLimit {
  let maximum = 0;
  let last = 0;

  return {
    maximum: () => maximum,
    raise(seconds) {
      maximum = Math.max(maximum, seconds);
    },
    placeAt(seconds) {
      last = seconds;
    },
    played(seconds) {
      const step = seconds - last;
      if (step >= 0 && step <= playingStep) {
        maximum = Math.max(maximum, seconds);
      }
      last = seconds;
    },
    seek(seconds) {
      if (!toValue(free) && seconds > maximum + seekTolerance) {
        last = maximum;
        return maximum;
      }
      last = seconds;
      return undefined;
    },
  };
}
