// A stand-in for the browser's XMLHttpRequest in the upload tests: it records what the code under test asked of it
// (method, URL, headers, the body handed to send) and the test drives it from there (progress, a status, a network
// error, a timeout). It is put in place with vi.stubGlobal('XMLHttpRequest', FakeXhr). Each event reaches both the
// listeners added with addEventListener and the on… property, as the browser's does.

type Handler = ((event: Event) => void) | null;

/** xhr.upload: where the browser reports how much of the body went out. */
export class FakeXhrUpload extends EventTarget {
  onprogress: Handler = null;
  onload: Handler = null;
  onerror: Handler = null;
  onabort: Handler = null;
  onloadend: Handler = null;

  /** Reports that loaded of total bytes went out. */
  progress(loaded: number, total: number): void {
    const event = new ProgressEvent('progress', { lengthComputable: true, loaded, total });
    this.dispatchEvent(event);
    this.onprogress?.(event);
  }
}

export class FakeXhr extends EventTarget {
  static readonly UNSENT = 0;
  static readonly OPENED = 1;
  static readonly HEADERS_RECEIVED = 2;
  static readonly LOADING = 3;
  static readonly DONE = 4;

  /** Every request made since the last reset, in order. */
  static instances: FakeXhr[] = [];

  static reset(): void {
    FakeXhr.instances = [];
  }

  /** The one request made; fails the test when there is none or more than one. */
  static only(): FakeXhr {
    const [only, ...more] = FakeXhr.instances;
    if (only === undefined || more.length > 0) {
      throw new Error(`Expected one XMLHttpRequest, found ${FakeXhr.instances.length}.`);
    }
    return only;
  }

  readonly upload = new FakeXhrUpload();
  method: string | undefined;
  url: string | undefined;
  /** The request headers as they were set, by lower-case name. */
  readonly headers: Record<string, string> = {};
  /** What send was given, exactly that object. */
  body: unknown;
  sent = false;
  aborted = false;
  readyState = FakeXhr.UNSENT;
  status = 0;
  responseText = '';
  timeout = 0;
  withCredentials = false;

  onload: Handler = null;
  onerror: Handler = null;
  ontimeout: Handler = null;
  onabort: Handler = null;
  onloadend: Handler = null;
  onreadystatechange: Handler = null;

  constructor() {
    super();
    FakeXhr.instances.push(this);
  }

  open(method: string, url: string | URL): void {
    this.method = method;
    this.url = String(url);
    this.readyState = FakeXhr.OPENED;
  }

  setRequestHeader(name: string, value: string): void {
    this.headers[name.toLowerCase()] = value;
  }

  send(body?: unknown): void {
    this.body = body;
    this.sent = true;
  }

  abort(): void {
    this.aborted = true;
    this.readyState = FakeXhr.DONE;
    this.status = 0;
    this.fire('readystatechange');
    this.fire('abort');
    this.fire('loadend');
  }

  getAllResponseHeaders(): string {
    return '';
  }

  getResponseHeader(): string | null {
    return null;
  }

  /** The storage answered with the status (201 when the blob was created). */
  respond(status: number): void {
    this.status = status;
    this.readyState = FakeXhr.DONE;
    this.fire('readystatechange');
    this.fire('load');
    this.fire('loadend');
  }

  /** The request failed on the network (offline, CORS refused, connection reset). */
  failOnNetwork(): void {
    this.readyState = FakeXhr.DONE;
    this.fire('readystatechange');
    this.fire('error');
    this.fire('loadend');
  }

  /** The request ran out of time. */
  timeOut(): void {
    this.readyState = FakeXhr.DONE;
    this.fire('readystatechange');
    this.fire('timeout');
    this.fire('loadend');
  }

  private fire(type: 'readystatechange' | 'load' | 'error' | 'timeout' | 'abort' | 'loadend'): void {
    const event = new Event(type);
    this.dispatchEvent(event);
    const handlers: Record<typeof type, Handler> = {
      readystatechange: this.onreadystatechange,
      load: this.onload,
      error: this.onerror,
      timeout: this.ontimeout,
      abort: this.onabort,
      loadend: this.onloadend,
    };
    handlers[type]?.(event);
  }
}

/** A File of the size given that holds no bytes: a 500 MB video costs nothing in a test, and reading it shows. */
export function fileOfSize(name: string, type: string, size: number): File {
  const file = new File([], name, { type });
  Object.defineProperty(file, 'size', { value: size });
  return file;
}
