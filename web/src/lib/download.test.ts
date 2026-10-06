import { afterEach, describe, expect, it, vi } from 'vitest';

import { fileNameFrom, saveFile } from './download';

describe('fileNameFrom (021, web test 20)', () => {
  it.each([
    [
      "attachment; filename=lancamentos-2026-09-01-a-2026-09-30.csv; filename*=UTF-8''lancamentos-2026-09-01-a-2026-09-30.csv",
      'lancamentos-2026-09-01-a-2026-09-30.csv',
    ],
    ["attachment; filename*=UTF-8''lan%C3%A7amentos.csv; filename=lancamentos.csv", 'lançamentos.csv'],
    ['attachment; filename="lancamentos desde 2026.csv"', 'lancamentos desde 2026.csv'],
    ['attachment; filename=lancamentos-ate-2026-09-30.csv', 'lancamentos-ate-2026-09-30.csv'],
  ])('reads %s', (header, expected) => {
    expect(fileNameFrom(header, 'lancamentos.csv')).toBe(expected);
  });

  it.each([null, '', 'attachment', "attachment; filename*=UTF-8''%E0%A4%A"])('falls back for %s', (header) => {
    expect(fileNameFrom(header, 'lancamentos.csv')).toBe('lancamentos.csv');
  });
});

describe('saveFile', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('clicks a temporary link naming the file, and lets the URL go afterwards', () => {
    vi.useFakeTimers();
    const createObjectURL = vi.fn(() => 'blob:export');
    const revokeObjectURL = vi.fn();
    Object.assign(URL, { createObjectURL, revokeObjectURL });

    const clicked: { download: string; href: string; attached: boolean }[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push({ download: this.download, href: this.href, attached: this.isConnected });
    });

    const blob = new Blob(['Data;Descrição'], { type: 'text/csv' });
    saveFile(blob, 'lancamentos.csv');

    expect(createObjectURL).toHaveBeenCalledWith(blob);
    expect(clicked).toEqual([{ download: 'lancamentos.csv', href: 'blob:export', attached: true }]);
    expect(document.querySelector('a[download]')).toBeNull();

    vi.runAllTimers();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:export');
  });
});
