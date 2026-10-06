import { ApiError } from '@/api/finance';

/** A refused write, split between a form's fields and the sentence above it. */
export interface Refusal {
  /** Messages for the fields the form renders, shown under each. */
  fields: Record<string, string[]>;
  /** The sentence shown above the form, never the English default title of a 400. */
  message: string | null;
}

/**
 * A refused write for a form that renders `shown`: a 400's messages for those fields go
 * under them, any other field's messages become the sentence, and a refusal that names
 * no field is its own message.
 */
export function refusal(error: Error, shown: readonly string[]): Refusal {
  const fields = error instanceof ApiError ? error.fields : {};
  const elsewhere = Object.entries(fields)
    .filter(([field]) => !shown.includes(field))
    .flatMap(([, messages]) => messages);

  return {
    fields,
    message: Object.keys(fields).length === 0 ? error.message : elsewhere.length > 0 ? elsewhere.join(' ') : null,
  };
}

/** The sentence a refusal becomes where no field is on screen: a 400's messages, or the problem's detail. */
export function refusalMessage(error: unknown): string {
  return error instanceof Error ? (refusal(error, []).message ?? error.message) : 'Algo deu errado.';
}
