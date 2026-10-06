/**
 * The file name a `Content-Disposition` header gives: RFC 6266's `filename*` first, which
 * may carry any character, then the plain `filename`, quoted or bare. `fallback` when the
 * header names none or names it undecodably.
 */
export function fileNameFrom(header: string | null, fallback: string): string {
  const extended = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(header ?? '')?.[1];

  if (extended) {
    try {
      return decodeURIComponent(extended.trim());
    } catch {
      return fallback;
    }
  }

  const plain = /filename\s*=\s*(?:"([^"]*)"|([^;]+))/i.exec(header ?? '');

  return (plain?.[1] ?? plain?.[2])?.trim() || fallback;
}

/**
 * Saves `blob` as a download named `fileName`, through a link that is clicked and removed
 * at once. The object URL is released a minute later rather than straight away, because
 * some browsers still read it after `click()` returns.
 */
export function saveFile(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');

  link.href = url;
  link.download = fileName;
  // Attached, because Firefox ignores a click on a detached link.
  document.body.append(link);
  link.click();
  link.remove();

  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
