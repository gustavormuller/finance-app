import type { PeriodReturn } from '@/api/finance';
import { benchmarkCodes } from '@/lib/labels';

/**
 * The benchmark codes the API sent, in the spec's order, any code the client does not
 * know appended as sent. The API orders them by code; the screen does not.
 */
export function orderedCodes(benchmarks: Record<string, PeriodReturn | null>): string[] {
  const sent = Object.keys(benchmarks);

  return [...benchmarkCodes.filter((code) => sent.includes(code)), ...sent.filter((code) => !benchmarkCodes.includes(code))];
}
