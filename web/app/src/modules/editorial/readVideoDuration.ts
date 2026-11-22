// A video chapter's duration, read from the file's metadata alone (spec 2.4): a <video preload="metadata"> on an object
// URL of the File, never its bytes in memory. Whole seconds, rounded up; undefined when the browser cannot tell within
// 10 seconds. The object URL is revoked whatever came of it.

const GiveUpAfterMilliseconds = 10_000;

export function readVideoDuration(file: File): Promise<number | undefined> {
  return new Promise<number | undefined>((resolve) => {
    const player = document.createElement('video');
    const objectUrl = URL.createObjectURL(file);
    let settled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const finish = (duration: number | undefined) => {
      if (settled) {
        return;
      }
      settled = true;
      clearTimeout(timer);
      player.removeEventListener('loadedmetadata', onLoaded);
      player.removeEventListener('error', onError);
      player.removeAttribute('src');
      URL.revokeObjectURL(objectUrl);
      resolve(duration);
    };
    const onLoaded = () => {
      const duration = player.duration;
      finish(Number.isFinite(duration) && duration > 0 ? Math.ceil(duration) : undefined);
    };
    const onError = () => finish(undefined);
    player.preload = 'metadata';
    player.addEventListener('loadedmetadata', onLoaded);
    player.addEventListener('error', onError);
    player.src = objectUrl;
    // The 10 seconds count from the turn the loading started in, once the caller has its promise.
    void Promise.resolve().then(() => {
      if (!settled) {
        timer = setTimeout(() => finish(undefined), GiveUpAfterMilliseconds);
      }
    });
  });
}
