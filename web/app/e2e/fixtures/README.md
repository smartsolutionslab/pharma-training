# End-to-end fixtures

## clip-20s.mp4

The video chapter's clip in `e2e/learning-player.spec.ts` (#44).

- 73,824 bytes; SHA-256 `f9943685d57d9c8cae9a7fb56d150e8e8835044f9e229f0ddb3f0ce8b4c561a5`.
- AV1, main profile (`av01.0.00M.08`), 160×90, 10 fps, 20 s, no audio; MP4 with major brand `isom` (compatible `isom av01 iso2 mp41`), which the host's chapter upload accepts.
- Made with ffmpeg 6.1.1 and SVT-AV1 1.7.0:

```sh
ffmpeg -f lavfi -i testsrc2=size=160x90:rate=10:duration=20 \
  -c:v libsvtav1 -preset 12 -crf 50 -an -movflags +faststart e2e/fixtures/clip-20s.mp4
```

Playwright's bundled Chromium (157.0.8103.78) and Google Chrome (153.0.8010.12) both answered `probably` to `canPlayType('video/mp4; codecs="av01.0.00M.08"')`, loaded its metadata (duration 20) and played it, probed on 2026-11-23.
