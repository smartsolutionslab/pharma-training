import { vi } from 'vitest';

/**
 * jsdom has no IntersectionObserver. This one records what it observes, and a test says when an element comes into
 * view with `FakeIntersectionObserver.show()`: every observer is called back for every element it observes, as the
 * browser calls back once the element is on the screen (at once, for a text that fits on it).
 */
export class FakeIntersectionObserver {
  static readonly instances: FakeIntersectionObserver[] = [];

  readonly root = null;
  readonly rootMargin = '0px';
  readonly thresholds: readonly number[] = [0];
  readonly observed = new Set<Element>();
  private readonly callback: IntersectionObserverCallback;

  constructor(callback: IntersectionObserverCallback) {
    this.callback = callback;
    FakeIntersectionObserver.instances.push(this);
  }

  observe(target: Element): void {
    this.observed.add(target);
  }

  unobserve(target: Element): void {
    this.observed.delete(target);
  }

  disconnect(): void {
    this.observed.clear();
  }

  takeRecords(): IntersectionObserverEntry[] {
    return [];
  }

  /** Every element observed now counts as in view (or, with false, out of it), as the browser would report it. */
  static show(isIntersecting = true): void {
    for (const observer of FakeIntersectionObserver.instances) {
      const entries = [...observer.observed].map((target) => entryFor(target, isIntersecting));
      if (entries.length > 0) {
        observer.callback(entries, observer as unknown as IntersectionObserver);
      }
    }
  }

  /** How many elements are observed across every observer: the end marker is watched once it is. */
  static observedCount(): number {
    return FakeIntersectionObserver.instances.reduce((count, observer) => count + observer.observed.size, 0);
  }

  /** Puts the fake in the browser's place for the test file; each test starts with no observers. */
  static install(): void {
    vi.stubGlobal('IntersectionObserver', FakeIntersectionObserver);
  }

  static reset(): void {
    FakeIntersectionObserver.instances.length = 0;
  }
}

function entryFor(target: Element, isIntersecting: boolean): IntersectionObserverEntry {
  const rect = target.getBoundingClientRect();
  return {
    target,
    isIntersecting,
    intersectionRatio: isIntersecting ? 1 : 0,
    boundingClientRect: rect,
    intersectionRect: rect,
    rootBounds: null,
    time: 0,
  };
}
