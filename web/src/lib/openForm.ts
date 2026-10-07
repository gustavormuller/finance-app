import { useRef, useState } from 'react';

/** One opening of a form: a new object each time, so two openings of the same form differ. */
export interface Opening<T> {
  readonly of: T;
}

/**
 * The form a page has open, if any. A save carries the opening it was sent from and, when it
 * lands, closes the form only if that opening is still the open one: while it was in flight the
 * person may have cancelled it, or opened another form, and a late answer must not close what
 * they are looking at.
 */
export function useOpenForm<T>() {
  const [open, setOpen] = useState<Opening<T> | null>(null);
  // Read when a save lands, which can be renders after the one that sent it.
  const latest = useRef<Opening<T> | null>(null);

  const set = (next: Opening<T> | null) => {
    latest.current = next;
    setOpen(next);
  };

  return {
    /** The opening on screen, or null. */
    open,
    /** Opens a form for `of`: a new opening, even of a form already open. */
    show: (of: T) => set({ of }),
    /** Closes whatever form is open, as Cancelar does. */
    close: () => set(null),
    /** The opening as of now, for a save that lands after other renders. */
    current: () => latest.current,
  };
}
